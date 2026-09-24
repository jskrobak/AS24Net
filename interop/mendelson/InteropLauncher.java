import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import de.mendelson.comm.as2.message.AS2Message;
import de.mendelson.comm.as2.message.AS2MessageInfo;
import de.mendelson.comm.as2.message.AS2Payload;
import de.mendelson.comm.as2.message.MessageAccessDB;
import de.mendelson.comm.as2.message.MessageCompressionType;
import de.mendelson.comm.as2.message.MessageDirectionType;
import de.mendelson.comm.as2.message.MessageOverviewFilter;
import de.mendelson.comm.as2.partner.Partner;
import de.mendelson.comm.as2.partner.PartnerAccessDB;
import de.mendelson.comm.as2.sendorder.SendOrderSender;
import de.mendelson.comm.as2.server.AS2Server;
import de.mendelson.util.database.IDBDriverManager;
import de.mendelson.util.security.BCCryptoHelper;
import de.mendelson.util.security.cert.CertificateManager;
import de.mendelson.util.security.cert.KeystoreCertificate;
import de.mendelson.util.security.cert.KeystoreStorageImplDB;
import de.mendelson.util.systemevents.SystemEventManagerImplAS2;

import java.io.FileInputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.util.HashMap;
import java.util.HexFormat;
import java.util.List;
import java.util.Map;
import java.util.logging.Logger;

/**
 * Mendelson opensource AS2 for the interoperability tests of AS24Net, without its GUI: starts the server, creates
 * the stations of the tests written by the test runner (/config/stations.tsv) and answers on port 8081:
 * <pre>
 *   POST /send?from=&amp;to=&amp;fileName=   sends the body from the local station to the partner, returns the Message-ID
 *   GET  /messages                      every message with its direction, state and the SHA-256 of its payloads
 *   GET  /health                        200 once everything is set up
 * </pre>
 * The keystore (certificates.p12, password "test") is prepared by entrypoint.sh and taken into the new database.
 */
public final class InteropLauncher {

    private static final Logger LOGGER = Logger.getLogger("interop");
    private static IDBDriverManager db;
    private static CertificateManager certificates;
    private static volatile boolean ready;

    public static void main(String[] args) throws Exception {
        System.setProperty("java.awt.headless", "true");
        startControl();

        new BCCryptoHelper().initialize();
        // HTTP server on, connections of clients from localhost only, no plugins, no forced keystore import: the
        // forced one only updates keystore data already in the database, while without it certificates.p12 is taken
        // into a new database, which the database of a new container is.
        new AS2Server(true, false, false, false, false);
        db = AS2Server.getActivatedDBDriverManager();

        certificates = new CertificateManager(LOGGER);
        certificates.loadKeystoreCertificates(new KeystoreStorageImplDB(SystemEventManagerImplAS2.instance(), db,
                KeystoreStorageImplDB.KEYSTORE_USAGE_ENC_SIGN, KeystoreStorageImplDB.KEYSTORE_STORAGE_TYPE_PKCS12));

        configure(Path.of("/config/stations.tsv"));
        ready = true;
        LOGGER.info("Interop launcher ready");
    }

    /** One line per case: local AS2 name, partner AS2 name, partner URL, signature, encryption, compress, sync MDN, signed MDN. */
    private static void configure(Path file) throws Exception {
        String own = fingerprint("/certs/mendelson.pem");
        String theirs = fingerprint("/certs/as24net.pem");
        PartnerAccessDB partners = new PartnerAccessDB(db);

        for (String line : Files.readAllLines(file)) {
            if (line.isBlank()) {
                continue;
            }
            String[] f = line.split("\t");
            Partner local = partnerFor(partners, f[0]);
            local.setLocalStation(true);
            local.setMdnURL("http://mendelson:8080/as2/HttpReceiver");
            local.setSignFingerprintSHA1(own);
            local.setCryptFingerprintSHA1(own);
            save(partners, local);

            Partner remote = partnerFor(partners, f[1]);
            remote.setLocalStation(false);
            remote.setURL(f[2]);
            remote.setSignType(signature(f[3]));
            remote.setEncryptionType(encryption(f[4]));
            remote.setCompressionType("1".equals(f[5]) ? MessageCompressionType.ZLIB : MessageCompressionType.NONE);
            remote.setSyncMDN("1".equals(f[6]));
            remote.setSignedMDN("1".equals(f[7]));
            remote.setSignFingerprintSHA1(theirs);
            remote.setCryptFingerprintSHA1(theirs);
            remote.setContentType("application/octet-stream");
            remote.setKeepOriginalFilenameOnReceipt(true);
            remote.setEnableDirPoll(false);
            save(partners, remote);
        }
    }

    private static Partner partnerFor(PartnerAccessDB partners, String as2Id) {
        Partner partner = partners.getPartnerByAS2Id(as2Id);
        if (partner == null) {
            partner = new Partner();
            partner.setAS2Identification(as2Id);
            partner.setName(as2Id);
            partner.setEmail(as2Id.toLowerCase() + "@interop.as24net");
        }
        return partner;
    }

    private static void save(PartnerAccessDB partners, Partner partner) {
        if (partner.getDBId() > 0) {
            partners.updatePartner(partner);
        } else {
            partners.insertPartner(partner);
        }
    }

    private static int signature(String name) {
        return switch (name) {
            case "none" -> AS2Message.SIGNATURE_NONE;
            case "sha1" -> AS2Message.SIGNATURE_SHA1;
            case "sha-384" -> AS2Message.SIGNATURE_SHA384;
            case "sha-512" -> AS2Message.SIGNATURE_SHA512;
            default -> AS2Message.SIGNATURE_SHA256;
        };
    }

    private static int encryption(String name) {
        return switch (name) {
            case "none" -> AS2Message.ENCRYPTION_NONE;
            case "3des" -> AS2Message.ENCRYPTION_3DES;
            case "aes128-cbc" -> AS2Message.ENCRYPTION_AES_128;
            case "aes192-cbc" -> AS2Message.ENCRYPTION_AES_192;
            default -> AS2Message.ENCRYPTION_AES_256;
        };
    }

    private static String fingerprint(String pemFile) throws Exception {
        try (InputStream in = new FileInputStream(pemFile)) {
            X509Certificate certificate = (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(in);
            KeystoreCertificate keystoreCertificate = new KeystoreCertificate();
            keystoreCertificate.setCertificate(certificate, null);
            return keystoreCertificate.getFingerPrintSHA1();
        }
    }

    // Control API

    private static void startControl() throws Exception {
        HttpServer server = HttpServer.create(new InetSocketAddress(8081), 0);
        server.createContext("/health", exchange -> reply(exchange, ready ? 200 : 503, "{\"ready\":" + ready + "}"));
        server.createContext("/messages", exchange -> {
            try {
                reply(exchange, 200, messages());
            } catch (Exception e) {
                reply(exchange, 500, json(e.toString()));
            }
        });
        server.createContext("/send", exchange -> {
            try {
                reply(exchange, 200, send(exchange));
            } catch (Exception e) {
                reply(exchange, 500, "{\"error\":" + json(e.toString()) + "}");
            }
        });
        server.start();
    }

    private static String send(HttpExchange exchange) throws Exception {
        Map<String, String> query = query(exchange.getRequestURI().getRawQuery());
        PartnerAccessDB partners = new PartnerAccessDB(db);
        Partner sender = partners.getPartnerByAS2Id(query.get("from"));
        Partner receiver = partners.getPartnerByAS2Id(query.get("to"));
        if (sender == null || receiver == null) {
            throw new IllegalArgumentException("Unknown station " + query.get("from") + " or partner " + query.get("to"));
        }

        Path directory = Files.createTempDirectory("send");
        Path file = directory.resolve(query.get("fileName"));
        try (InputStream in = exchange.getRequestBody()) {
            Files.write(file, in.readAllBytes());
        }
        AS2Message message = new SendOrderSender(db).send(certificates, sender, receiver, file, null,
                "AS2 interop test " + file.getFileName(), null, null);
        if (message == null) {
            throw new IllegalStateException("Mendelson did not create the message, see its log");
        }
        return "{\"messageId\":" + json(message.getAS2Info().getMessageId()) + "}";
    }

    private static String messages() throws Exception {
        MessageAccessDB access = new MessageAccessDB(db);
        MessageOverviewFilter filter = new MessageOverviewFilter();
        filter.setShowDirection(MessageDirectionType.ALL);
        filter.setLimit(5000);
        StringBuilder json = new StringBuilder("[");
        for (AS2MessageInfo info : access.getMessageOverview(filter)) {
            if (json.length() > 1) {
                json.append(',');
            }
            json.append("{\"messageId\":").append(json(info.getMessageId()))
                    .append(",\"direction\":").append(json(String.valueOf(info.getDirection())))
                    .append(",\"state\":").append(json(String.valueOf(info.getState())))
                    .append(",\"from\":").append(json(info.getSenderId()))
                    .append(",\"to\":").append(json(info.getReceiverId()))
                    .append(",\"payloads\":[");
            List<AS2Payload> payloads = access.getPayload(info.getMessageId());
            for (int i = 0; i < payloads.size(); i++) {
                AS2Payload payload = payloads.get(i);
                String path = payload.getPayloadFilename();
                String sha = path != null && Files.exists(Path.of(path))
                        ? HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(Path.of(path))))
                        : null;
                json.append(i > 0 ? "," : "").append("{\"fileName\":").append(json(payload.getOriginalFilename()))
                        .append(",\"sha256\":").append(json(sha)).append('}');
            }
            json.append("]}");
        }
        return json.append(']').toString();
    }

    private static Map<String, String> query(String raw) {
        Map<String, String> result = new HashMap<>();
        if (raw != null) {
            for (String pair : raw.split("&")) {
                String[] parts = pair.split("=", 2);
                result.put(URLDecoder.decode(parts[0], StandardCharsets.UTF_8),
                        parts.length > 1 ? URLDecoder.decode(parts[1], StandardCharsets.UTF_8) : "");
            }
        }
        return result;
    }

    private static String json(String value) {
        if (value == null) {
            return "null";
        }
        StringBuilder result = new StringBuilder("\"");
        for (char c : value.toCharArray()) {
            switch (c) {
                case '"' -> result.append("\\\"");
                case '\\' -> result.append("\\\\");
                case '\n' -> result.append("\\n");
                case '\r' -> result.append("\\r");
                case '\t' -> result.append("\\t");
                default -> result.append(c < 0x20 ? String.format("\\u%04x", (int) c) : String.valueOf(c));
            }
        }
        return result.append('"').toString();
    }

    private static void reply(HttpExchange exchange, int status, String body) throws java.io.IOException {
        byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
        exchange.getResponseHeaders().set("Content-Type", "application/json");
        exchange.sendResponseHeaders(status, bytes.length);
        try (OutputStream out = exchange.getResponseBody()) {
            out.write(bytes);
        }
    }
}
