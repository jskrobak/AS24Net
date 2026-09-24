# AS24Net Server

[![CI](https://github.com/jskrobak/AS24Net/actions/workflows/ci.yml/badge.svg)](https://github.com/jskrobak/AS24Net/actions/workflows/ci.yml)

AS2 (Applicability Statement 2, [RFC 4130](https://www.rfc-editor.org/rfc/rfc4130)) server and client with a Blazor
administration UI. Runs on .NET 10 with PostgreSQL. A sibling of [Oftp4Net](https://github.com/jskrobak/Oftp4Net):
the same architecture, the same administration and the same colours, for partners that exchange their EDI over
HTTP instead of OFTP2.

## Features

- Sending messages from a send queue to partners over HTTP or HTTPS, several at the same time, with retries and
  exponential back-off
- Receiving messages on the AS2 endpoint `POST /as2`, with the partner recognised by its AS2 name and
  authenticated by its signature
- S/MIME per partner: signing (SHA-1, SHA-256, SHA-384, SHA-512), encryption (AES-256/192/128-CBC, 3DES) and zlib
  compression (RFC 5402), before or after signing; received messages opened whatever the order of the layers
- MDNs in both directions: synchronous (in the HTTP response) and asynchronous (posted to a URL), signed or not,
  with the MIC compared with ours; asynchronous MDNs posted with retries
- Duplicates recognised by their Message-ID (AS2 reliability): confirmed again, not stored again; messages whose
  asynchronous MDN does not come in time are sent again with the same Message-ID
- Security requirements per partner: messages that are not signed or not encrypted can be refused
  (`insufficient-message-security`)
- Certificates of a partner uploaded in advance with the date and time from which they are used; the certificate
  it replaces is still accepted for signatures on their way, and the change is logged, run as a hook and reported by
  webhook
- HTTPS with the system trust store, the partner's own (pinned) certificate or its CA; HTTP basic authentication
- REST API with bearer tokens to put messages into the queue and fetch received ones, with webhooks and an
  interactive documentation (OpenAPI, Scalar)
- Scripts run on events (hooks), webhooks per message, per API token and for the whole server
- Persistent transfer log: outgoing, incoming, MDNs, certificate changes, hooks and webhooks
- Web UI: identities, partners, certificate changes, certificates, outgoing and received messages, settings, API
  tokens, users and a live log, in a light and a dark theme
- Signing in with a password or with Microsoft Entra ID, where the list of users decides who may come in
- Health checks for Docker, Kubernetes and monitoring: database, storage, send service, certificates, certificate
  changes and stuck messages, shown on the dashboard and reported by webhook
- Retention: old data removed every night and written to compressed archive files first, nothing unfinished touched
- A development setup with two stations that are each other's partner on one server, to see a message go the whole
  way at once

## AS2 support

What of RFC 4130 (and the standards it builds on) the implementation covers:

| Area | State |
|---|---|
| Transport | HTTP and HTTPS (TLS 1.2 / 1.3), `POST` of the MIME body with the AS2 headers; basic authentication to the partner |
| AS2 headers | `AS2-Version: 1.2`, `AS2-From` / `AS2-To` (quoted when needed), `Message-ID`, `Subject`, `EDIINT-Features` |
| Signing | `multipart/signed` with a detached `application/pkcs7-signature` (CMS SignedData), SHA-1 / SHA-2; verification over the exact bytes received, with a fallback to CRLF line ends |
| Encryption | `application/pkcs7-mime; smime-type=enveloped-data` (CMS EnvelopedData, RSA key transport), AES-CBC and 3DES; DER or base64 bodies |
| Compression | `application/pkcs7-mime; smime-type=compressed-data` (CMS CompressedData, zlib, RFC 3274 / RFC 5402), before or after signing |
| MIC | Computed over the signed content (with the digest of the signature), over the decrypted entity of an unsigned encrypted message, or over the content of an unsecured one (RFC 4130 7.3.1) |
| MDN | `multipart/report; report-type=disposition-notification`, signed or not; `processed`, `processed/warning` and `processed/error` dispositions with the error modifiers of RFC 4130 and RFC 5402 |
| MDN delivery | Synchronous in the HTTP response, asynchronous to `Receipt-Delivery-Option` over HTTP(S) |
| Reliability | Duplicates by Message-ID confirmed as `processed/warning: duplicate-document`; resend with the same Message-ID when an asynchronous MDN does not come |

Not implemented, because the deployments this server is built for do not use it:

- Multiple attachments in one message (`EDIINT-Features: multiple-attachments`)
- Certificate Exchange Messaging (CEM) as a protocol; certificates are exchanged out of band and scheduled here
- RSA-PSS signatures and RSA-OAEP key transport
- Asynchronous MDNs by e-mail (SMTP)

Interoperability was tested against [pyas2lib](https://github.com/abhishek-ram/pyas2lib) in both directions: signed,
encrypted and compressed messages with every supported algorithm, synchronous and asynchronous signed MDNs.

## Solution structure

| Project | Content |
|---|---|
| `AS24Net.Core` | AS2 protocol library without application dependencies: MIME parser and writer keeping the exact bytes, S/MIME signing and encryption, CMS compression, MIC, MDN building and parsing, AS2 headers |
| `AS24Net.Core.Tests` | MIME, security and message round trips in every combination of the options |
| `AS24Net.Domain` | Entities |
| `AS24Net.Entity` | EF Core DbContext (PostgreSQL) and migrations |
| `AS24Net.DataLayer` | Repositories and filters (Havit.Data patterns) |
| `AS24Net.Services` | Send queue, inbound processing, asynchronous MDNs, certificate changes, hooks, webhooks, health checks, retention |
| `AS24Net.Services.Tests` | Tests of the services |
| `AS24Net.DependencyInjection` | Data layer registration |
| `AS24Net.Server` | Blazor Server UI, AS2 endpoint, REST API and host |

## Configuration

| Key | Description |
|---|---|
| `ConnectionStrings:AS24Net` | PostgreSQL connection string (required) |
| `Database:MigrateOnStartup` | Create / update the database schema on startup (default `true`) |
| `DataProtection:KeysDirectory` | Keys encrypting the auth cookie and the secrets stored in the database (certificate passwords, HTTP passwords of partners, webhook secrets). **Back them up together with the database**, without them the stored secrets cannot be decrypted. |
| `DataDirectory` | Base directory for relative receive / outbox / archive directories (default: current directory, `/data` in Docker) |
| `TimeZone` | Time zone of the application (IANA name, default `Europe/Prague`, `Local` for the one of the machine). All times are stored and shown in it, the times of certificate changes included. |
| `LogDirectory` | Directory of the rolling log file |
| `Upload:MaxOutboxFileSizeMB` | Maximum size of a payload put into the send queue through the UI or the REST API (default 512) |
| `Certificates:GenerateCertificate` | Create a self-signed certificate for signing and decryption on startup when there is none with a private key (default `true`) |
| `Certificates:CertificateSubject` | Subject (CN) of the generated certificate (default: machine / container name) |
| `ReverseProxy:TrustAll` | Trust `X-Forwarded-*` headers from any proxy |
| `HealthChecks:MinFreeDiskSpaceMB` | Free disk space below which the storage is reported as degraded (default 1024) |
| `HealthChecks:WebhookUrl`, `HealthChecks:WebhookSecret` | Webhook `health.changed` called when the state of a health check changes |
| `Webhooks:EventsUrl`, `Webhooks:EventsSecret` | Webhook called on every message and certificate event (see *Webhooks*) |
| `Webhooks:AllowPrivateNetworks` | Allow webhook URLs in private and loopback networks (default `false`) |
| `Webhooks:RetryDelaysSeconds` | Delays before retrying a failed webhook call (default `5,30,120`) |
| `Hooks:*` | Scripts run on events, see *Hooks* |

Runtime settings (public URL of the AS2 endpoint, directories, send interval, parallel transfers, retries, largest
message accepted, retention) are edited on the *Settings → General* page and stored in the database.

`appsettings.json` and `appsettings.{Environment}.json` belong to the repository, so secrets do not go there. They
belong into one of these, which are read after those files:

| Where | For |
|---|---|
| `appsettings.{Environment}.local.json` next to them | a machine that is set up by hand; `.gitignore` knows the name |
| `dotnet user-secrets set "Entra:ClientSecret" "…" --project AS24Net.Server` | development, stored in the profile of the user |
| environment variables, e.g. `Entra__ClientSecret` | containers and production, and they have the last word |

## Identities and partners

An **identity** is our own AS2 station: the AS2 name partners send to (their `AS2-To`, our `AS2-From`), the
certificate our messages and MDNs are signed with and the one partners encrypt for (often the same one). When the
decryption certificate is replaced, the previous one is still used to decrypt messages that were on their way.

A **partner** is a remote AS2 station:

| Setting | Meaning |
|---|---|
| *AS2 name*, *URL* | the partner's `AS2-From` / our `AS2-To`, and its AS2 endpoint our messages are posted to |
| *Default identity* | the identity we send from when a message names none |
| *Content type* | media type of the payload when a message names none, e.g. `application/edifact`, `application/edi-x12`, `application/xml` |
| *Sign*, *Signature digest* | our messages are signed with the identity's signing certificate |
| *Encrypt*, *Encryption* | our messages are encrypted for the partner's encryption certificate |
| *Compress*, *Compress before signing* | zlib compression of the payload (recommended) or of the signed message |
| *MDN* | none, synchronous or asynchronous; *Request a signed MDN*; an asynchronous MDN that does not come within *minutes* makes the message go again |
| *Must be signed*, *Must be encrypted* | messages of the partner that are not are refused with `insufficient-message-security` |
| *Signature*, *Encryption*, *HTTPS server* certificates | the partner's certificates; the one it signed with before the last change is still accepted |
| *HTTP user name / password*, *Timeout* | basic authentication and how long to wait for the answer (with a synchronous MDN, for the MDN) |

A partner is found by the `AS2-From` of its messages and our identity by their `AS2-To`; a message for an unknown
pair is refused with `unknown-trading-partner` (in a synchronous MDN only: an address from an unauthenticated
request is not posted to).

## Certificate changes

Partners announce a new certificate some time before they start using it. *Certificate changes* takes the new
certificate (`.cer`, `.crt`, `.pem`, or one stored already) with the date and time it is used from and what for:

| Used for | Replaces |
|---|---|
| *signature verification and encryption* | both certificates, the usual case of one certificate per partner |
| *signature verification* | the certificate the partner's messages and MDNs are verified with |
| *encryption* | the certificate our messages are encrypted for |
| *HTTPS (TLS) trust* | the certificate trusted for the partner's HTTPS server |

Until that moment the current certificate stays in use. At that moment (to the second: a background scheduler
sleeps until the next change) the new one is put in place. The signature certificate it replaces becomes the
*previous* one and is still accepted, so that messages and MDNs signed just before the change are not refused; it
can be dropped on the partner's page once the roll-over is over. A time that has passed applies the certificate
right away, and a scheduled change can be cancelled until its time.

Every change is written to *Logs → Certificates*, runs the hook `OnCertificateApplied` and calls the webhook
`certificate.applied`. The health check `certificate-changes` reports a change that is overdue (the scheduler would
be stuck), a certificate that expires before it is to be used and a change that failed; `certificates` does not
report a certificate that expires when a change replaces it in time.

The same through the REST API:

```bash
curl -H "Authorization: Bearer $TOKEN" -F file=@partner-2027.crt -F usage=SignatureAndEncryption \
     -F activateAt=2026-10-01T06:00 -F note="Announced by e-mail on 2026-09-20" \
     https://as2.example.com/api/v1/partners/PARTNER-AS2/certificate-changes
```

`activateAt` without an offset is the time of the application (`TimeZone`), with `Z` or an offset an instant.

## Users

Users of the administration UI are stored in the database (*Settings → Users*), passwords are hashed.
When the application starts with an empty database it creates the account **`admin` / `admin`**; the password has to be
changed after the first sign in.

### Signing in with Microsoft Entra ID

Users can sign in with their company account instead of a password. What the administrator does once, in this
order:

1. **Register an application** in Entra ID (*App registrations → New registration*) and add a redirect URI of the
   platform **Web**: `https://<the public address of the server>/signin-oidc`. No API permissions have to be
   granted, the default delegated ones are enough, and no administrator consent is needed.
2. **Create a client secret** (*Certificates & secrets*) and write down the directory (tenant) and application
   (client) identifiers with it.
3. **Configure the three values**:

   ```json
   "Entra": {
     "TenantId": "…",
     "ClientId": "…",
     "ClientSecret": "…"
   }
   ```

   The secret does not belong into `appsettings.json`, which is in the repository; put it into
   `appsettings.{Environment}.local.json`, the user secrets or an environment variable (`Entra__ClientSecret`),
   see *Configuration* above. Without the section nothing changes and the sign in page only asks for a password.
4. **Restart the application.** The sign in page now offers *Sign in with Microsoft*.
5. **Sign in with a password** and give every user their address on the *Users* page (the icon with the badge):
   the e-mail address or user principal name Entra ID knows them by. Until that is done nobody gets in that way,
   the administrator included — Entra ID says who somebody is, the user list says who may come in. An identity
   without a user is refused with a message that names the address, so it can be copied from there.
6. A user who is to sign in this way only is created **without a password** on the same page.

The address is compared with the claim `preferred_username` of Entra ID, and with `email` or `upn` when that is
missing; for a work account it is normally the user principal name.

The sign in with a password stays available, so that a wrong tenant or an expired secret cannot lock the
administrator out. Behind a reverse proxy set `ReverseProxy:TrustAll` (or the proxy's address), otherwise the
application builds the redirect from the internal address and Entra ID refuses it with `AADSTS50011`.

## Running locally

```bash
cd AS24Net.Server
dotnet run
```

The development configuration (`appsettings.Development.json`) uses PostgreSQL on `localhost:5432`
(database `as24net-dev`, user `root` / `root`) and listens on `http://localhost:5010`. The database and its schema
are created on startup; data (received and outgoing payloads, keys, logs) goes to `../data`, i.e. `data/` next to the
solution, which git ignores.

### Sending a message to yourself

In Development the database is also seeded (`SeedLoopback` in `appsettings.Development.json`) with two stations on
this server, each the partner of the other one, sharing a self-signed certificate:

- identities **Loopback A** (`AS24NET-A`) and **Loopback B** (`AS24NET-B`),
- partner **Loopback B** (sent from A, asynchronous MDN) and partner **Loopback A** (sent from B, synchronous MDN),
  both signing, encrypting and compressing, posting to `http://localhost:5010/as2`.

Set *Settings → General → Public URL* to `http://localhost:5010/as2` (needed for asynchronous MDNs), open
*Messages → Outgoing*, click *New message*, upload a file (e.g. [`samples/hello.edi`](samples/hello.edi)) and select
partner *Loopback A* or *Loopback B*. The message appears in *Messages → Received* and turns `Delivered` once the
MDN arrives; the logs show every step.

New migrations are created with the EF tool pinned in `dotnet-tools.json`:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project AS24Net.Entity
```

## Docker

Images for `linux/amd64` and `linux/arm64` are published to the GitHub Container Registry:
`latest` from the `main` branch, `X.Y.Z` / `X.Y` for release tags `vX.Y.Z` and `sha-…` for every build.

```bash
docker run -p 8080:8080 -v as24net-data:/data \
  -e ConnectionStrings__AS24Net="Host=db;Database=as24net;Username=as2;Password=..." \
  ghcr.io/jskrobak/as24net:latest
```

Port 8080 serves the administration, the REST API and the AS2 endpoint `/as2`. Put a reverse proxy with the public
HTTPS address in front of it and set that address with `/as2` as *Public URL* in the settings. The AS2 endpoint and
the health endpoints are not redirected to HTTPS, so that partners posting over plain HTTP (the messages are secured
by S/MIME) and probes keep working.

On the first start the container creates its own self-signed certificate (RSA 3072, 3 years) for signing and
decryption when the database contains no certificate with a private key; select it for your identities and download
its public part on the *Certificates* page to send it to partners, or import one of a certification authority.

All persistent data lives in the `/data` volume (`DataDirectory`): data protection keys, logs, the receive, outbox and
archive directories (relative paths in the settings are resolved against it).

To build the image locally:

```bash
docker build -f AS24Net.Server/Dockerfile -t as24net-server .
```

## AS2 endpoint

Partners post messages and asynchronous MDNs to `POST /as2` (a `GET` answers with a short text, so the address can
be checked in a browser). A request is:

1. an **MDN** of one of our messages (`multipart/report`, signed or not): it is verified with the partner's
   certificates, matched to the message by `Original-Message-ID` and sets its state; or
2. a **message**: the partner and the identity are found by `AS2-From` / `AS2-To`, the message is decrypted, its
   signature verified and it is decompressed; the payload is stored in the receive directory under
   `<receive directory>/<AS2 name of the partner>/<time>_<number>_<file name>` (written under a temporary name first,
   so that a program watching the directory never sees half a file). The answer is the synchronous MDN, or a short
   text when the MDN follows asynchronously or none was requested.

A message that cannot be processed gets an MDN with `processed/error:` and the reason (`decryption-failed`,
`authentication-failed`, `integrity-check-failed`, `decompression-failed`, `insufficient-message-security`,
`unknown-trading-partner`, `unexpected-processing-error`) and is kept in *Received* with the error. Messages larger
than *Largest message accepted* are refused with HTTP 413. An asynchronous MDN of a message whose signature was not
verified is posted only to the host of the partner's URL.

## Health checks

| Endpoint | Checks | Access |
|---|---|---|
| `GET /health/live` | none, the process answers | anonymous |
| `GET /health/ready` | `database`, `storage`, `send-service`; `503` when one of them is unhealthy | anonymous, the state only |
| `GET /health/details` | all of them, as JSON with a description and data | API token (`Authorization: Bearer …`) |

The liveness endpoint checks nothing on purpose: a database outage must not make the orchestrator restart the
server again and again. The Docker image uses it in its `HEALTHCHECK`; readiness is for the load balancer or a
Kubernetes readiness probe.

`live` and `ready` answer with the overall state as plain text, `Healthy`, `Degraded` or `Unhealthy`. The status
code is `200` for the first two and `503` for `Unhealthy`, so a probe fails only when the server cannot work; a
degraded state (e.g. the send service paused by an administrator) does not take it out of service.

```bash
curl -i http://localhost:8080/health/ready
curl -H "Authorization: Bearer <token>" http://localhost:8080/health/details
```

```json
{
  "status": "Degraded",
  "duration": 12.4,
  "checks": {
    "certificates": {
      "status": "Degraded",
      "description": "Certificate CN=partner.example.com (signature certificate of partner Partner) expires on 10/15/2026.",
      "duration": 10.3,
      "tags": [ "operational" ],
      "data": { "CN=partner.example.com (#3)": "valid to 2026-10-15T12:00:00; signature certificate of partner Partner" },
      "error": null
    }
  }
}
```

In Kubernetes:

```yaml
livenessProbe:
  httpGet: { path: /health/live, port: 8080 }
  periodSeconds: 30
  failureThreshold: 3
readinessProbe:
  httpGet: { path: /health/ready, port: 8080 }
  periodSeconds: 15
```

### The checks

| Check | Unhealthy / degraded when |
|---|---|
| `database` | the database cannot be reached or migrations are missing (`Database:MigrateOnStartup=false`); its size and the largest tables in bytes are in the data |
| `storage` | the receive or outbox directory or the data protection keys cannot be written; degraded when disk space runs low |
| `send-service` | the service stopped or has not processed the queue for three send intervals and a minute; degraded while paused |
| `certificates` | degraded: a certificate in use (identities, partners) expired or expires within 30 days, unless a scheduled change replaces it in time |
| `certificate-changes` | degraded: a scheduled change is more than 10 minutes overdue, its certificate expires before its time, or a change failed within 7 days |
| `messages` | degraded: a message waits to be sent for more than 24 hours, failed or was not delivered in the last 24 hours, an asynchronous MDN of ours could not be posted for an hour, or a message of a partner was refused in the last 24 hours |
| `internal-queues` | degraded: the queue of the transfer log, the webhooks or the hooks is 80 % full and about to drop items |
| `retention` | degraded: the nightly removal of old data failed or has not run for two days |

The checks read the state the services keep and the database; none of them connects to a partner. The application
runs them every 30 seconds, shows the result on the dashboard and writes every change to the log. With
`HealthChecks:WebhookUrl` configured, every change is also posted as the webhook `health.changed` with the overall
state in `status` and the checks that are not healthy in `error`.

## Transfer log

The *Logs* section shows the transfer history stored in the database, in views with filters (partner, file name or
Message-ID, severity, period) and a detail of every record:

| View | Records |
|---|---|
| Outgoing | messages sent, failed (with the next retry) and delivered without an MDN |
| Incoming | messages received, refused and received again (duplicates) |
| MDN | MDNs received for our messages (positive, negative, invalid, a different MIC, not in time) and MDNs we sent |
| Certificates | certificate changes of partners scheduled, applied, cancelled and failed |
| Hooks and webhooks | every hook run with exit code, duration and output, and every webhook call |

Records older than *Hide transfer log records after (days)* (setting, default 90) are shown only when *Complete
archive* is checked in the filter; how long they are kept at all is up to the retention below. *Log stream* remains
the live technical log.

## Retention

Every night old data is removed, so that the database stays within bounds (*Settings → Retention*):

| Setting | Default | What is removed |
|---|---|---|
| *Remove content after (days)* | 30 | the details of transfer log records (exceptions, output of hooks, bodies of webhooks; the record stays), the parameters of hook runs (*Run again* is not offered any more) and the payloads of delivered messages in the outbox |
| *Delete informational log records after (days)* | 90 | informational transfer log records |
| *Delete warnings and errors after (days)* | 365 | the remaining transfer log records |
| *Delete finished messages after (days)* | 365 | records of sent messages whose MDN arrived (`Delivered`, `NotDelivered`) and of received messages whose MDN was sent |

Nothing is lost on the way:

- Everything removed from the database is written to the archive directory first (*Archive directory*, default
  `archive` in the data directory): compressed JSON lines per kind and month, e.g.
  `transfer-log-2026-09.jsonl.gz`, `outgoing-messages-2026-09.jsonl.gz`, `received-messages-2026-09.jsonl.gz`.
  Back the directory up with the database; it is read with `zcat` or any gzip reader, e.g.
  `zcat archive/outgoing-messages-2026-*.jsonl.gz | grep INVOIC`. After a crash in the middle of a run a record may be
  there twice, never missing.
- Nothing unfinished is touched: messages waiting, failed for good or waiting for their MDN, and received messages
  whose asynchronous MDN has not been posted yet.
- Finished messages are kept at least 30 days whatever the setting says, because a message a partner sends again is
  recognised as a duplicate by its record.
- A payload in the outbox is deleted only when it belongs to delivered messages and to nothing else; files the
  application did not put there stay. The received payloads stay where they are, they belong to the integration.

The first run is a few minutes after the start, *Clean up now* runs it at once.

## REST API

Integrations can use a REST API authenticated with a bearer token: `Authorization: Bearer <token>`.
Tokens are created in *Settings → API tokens* and shown only once (only their hash is stored); they can be disabled,
deleted and given an expiry date.

*Settings → REST API* shows the interactive documentation (Scalar) with request examples in several languages;
the OpenAPI description itself is at `/openapi/v1.json`. Both require a signed in administrator.

| Endpoint | Purpose |
|---|---|
| `POST /api/v1/messages` | puts a message into the send queue (multipart: `file`, `partner`, optional `identity`, `fileName`, `contentType`, `subject`, `reference`, `webhookUrl`, `webhookSecret`) |
| `POST /api/v1/messages/raw` | the same with the payload as the body and its `Content-Type` (query: `partner`, `fileName`, optional `identity`, `subject`, `reference`, `webhookUrl`; header `X-Webhook-Secret`) |
| `GET /api/v1/messages` | lists messages (`status`, `partner`, `reference`, `from`, `to`, `skip`, `take`) |
| `GET /api/v1/messages/{id}` | detail of a message with its MDN (disposition, MIC) |
| `DELETE /api/v1/messages/{id}` | removes a message that has not been sent |
| `POST /api/v1/messages/{id}/retry` | puts a failed or finished message back into the queue (with a new Message-ID) |
| `GET /api/v1/inbox` | lists received messages; `onlyNew=true` returns messages not fetched yet |
| `GET /api/v1/inbox/{id}` / `…/content` | detail / payload of a received message |
| `POST /api/v1/inbox/{id}/fetched` | marks a received message as fetched |
| `GET /api/v1/partners`, `/identities` | names usable when sending |
| `GET`, `POST /api/v1/partners/{partner}/certificate-changes` | certificate changes of a partner; uploads a certificate with the time it is used from |
| `DELETE /api/v1/certificate-changes/{id}` | cancels a scheduled certificate change |
| `GET /api/v1/events` | reads the transfer log |
| `GET /api/v1/status` | state of the send service and the queues |

Partners and identities are given by their name or AS2 name.

```bash
curl -H "Authorization: Bearer $TOKEN" -F file=@orders.edi -F partner=PARTNER-AS2 \
     -F reference=ORDER-4711 -F webhookUrl=https://erp.example.com/as2/callback -F webhookSecret=$SECRET \
     https://as2.example.com/api/v1/messages

curl -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/xml" --data-binary @invoice.xml \
     "https://as2.example.com/api/v1/messages/raw?partner=PARTNER-AS2&fileName=invoice.xml"
```

### Webhooks

`webhookUrl` registered with a message is called on `message.sent`, `message.delivered` (positive MDN, or accepted
without an MDN), `message.not_delivered` (negative or invalid MDN, a different MIC of a signed message, no MDN in
time) and `message.failed` (sending failed for good). A token can carry an *inbox webhook URL*, called on
`message.received`. `Webhooks:EventsUrl` is called on all of these and on `message.refused` and
`certificate.applied`.

The request is a `POST` with a JSON body (`event`, `timestamp`, `outgoingMessageId` or `receivedMessageId`,
`messageId`, `reference`, `fileName`, `contentType`, `size`, `partnerName`, `partnerAs2Id`, `identityAs2Id`,
`status`, `disposition`, `mic`, `error`, `sentDate`, `deliveredDate`; only those with a value) and the header
`X-AS24Net-Event`. When a secret is set, the header `X-AS24Net-Signature` contains `sha256=<hex>`, the HMAC-SHA256
of the body; verify it before trusting the call. A call that fails is retried (`Webhooks:RetryDelaysSeconds`) and the
result is in *Logs → Hooks and webhooks*. URLs in private or loopback networks are refused unless
`Webhooks:AllowPrivateNetworks` is enabled.

## Hooks

A script or executable can be run on events. Hooks are configured in the application configuration (not in the web
UI, so that they cannot be changed from there), e.g. with environment variables:

| Setting | Runs when |
|---|---|
| `Hooks:OnReceived` | a message was received and its payload stored (before the MDN is returned) |
| `Hooks:OnReceiveFailed` | a received message could not be processed; the partner gets a negative MDN |
| `Hooks:OnSent` | the partner's server accepted a message |
| `Hooks:OnSendFailed` | sending a message failed (`AS2_WILL_RETRY` tells whether it will be retried) |
| `Hooks:OnMdnReceived` | the partner confirmed a message with a positive MDN (or accepted it when no MDN was requested) |
| `Hooks:OnNotDelivered` | the partner returned a negative MDN, or the MDN was invalid or did not come in time |
| `Hooks:OnCertificateApplied` | a scheduled certificate of a partner was put in place |
| `Hooks:TimeoutSeconds` | a script running longer is killed (default 60) |

Parameters are passed as environment variables and, with the same names in camel case, as a JSON object on
standard input:

| Variable | Events | Content |
|---|---|---|
| `AS2_EVENT`, `AS2_TIMESTAMP` | all | event name, time (ISO 8601) |
| `AS2_MESSAGE_ID` | messages | Message-ID |
| `AS2_FILE_NAME`, `AS2_FILE_PATH`, `AS2_CONTENT_TYPE`, `AS2_SIZE` | messages | payload |
| `AS2_PARTNER_NAME`, `AS2_PARTNER_AS2_ID`, `AS2_IDENTITY_AS2_ID` | all | partner and our identity |
| `AS2_RECEIVED_MESSAGE_ID`, `AS2_SUBJECT`, `AS2_SIGNED`, `AS2_ENCRYPTED` | received | record id, subject, security of the message |
| `AS2_OUTGOING_MESSAGE_ID`, `AS2_REFERENCE`, `AS2_STATUS`, `AS2_DISPOSITION` | sent | record id, reference of the caller, state, disposition of the MDN |
| `AS2_MIC` | messages | the MIC |
| `AS2_ERROR`, `AS2_WILL_RETRY` | failures | error message, whether it is retried |
| `AS2_CERTIFICATE_NAME`, `AS2_CERTIFICATE_THUMBPRINT`, `AS2_USAGE`, `AS2_ACTIVATE_AT` | certificate applied | the new certificate and what it is used for |
| `AS2_RUN_AGAIN_OF` | run again manually | id of the log record of the failed run |

Hooks run in the background one after another; a slow or failing script never affects a transfer. Their output and
exit code are logged. A failed run is not repeated automatically: *Run again* in its detail in *Logs → Hooks and
webhooks* queues the script now configured for the event again, with the same parameters. See
[`samples/hooks/on_received.sh`](samples/hooks/on_received.sh). In Docker, mount the scripts and point the
configuration to them:

```bash
docker run ... -v ./hooks:/scripts:ro -e Hooks__OnReceived=/scripts/on_received.sh ghcr.io/jskrobak/as24net:latest
```

## Setting up a partner

1. *Settings → General*: set the *Public URL* of the AS2 endpoint.
2. *Settings → Certificates*: import our certificate with its private key (`.pfx`, `.p12`) or use the generated one,
   and import the partner's certificate (`.cer`, `.crt`, `.pem`).
3. *Identities*: create our station with its AS2 name and select our certificate for signing and decryption.
4. Give the partner our AS2 name, the public URL and our certificate (*Certificates*, the download icon).
5. *Partners*: create the partner with its AS2 name and URL, select its certificates, and set the security and the
   MDN as agreed with it.
6. *Messages → Outgoing*: send a first message and watch it turn `Delivered` with the MDN.

## Tests

```bash
dotnet test
```

## License

[MIT](LICENSE)
