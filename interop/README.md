# Interoperability and load tests

AS24Net against other AS2 implementations, and under load, with everything in Docker. The tests are run by hand;
the normal CI only builds the runner.

```bash
dotnet run --project interop/AS24Net.Interop -- interop            # all stations
dotnet run --project interop/AS24Net.Interop -- interop --stations pyas2,openas2 --cases sha256-aes256,async-mdn
dotnet run --project interop/AS24Net.Interop -- load inbound --messages 2000 --concurrency 32
dotnet run --project interop/AS24Net.Interop -- load outbound --messages 2000 --size 102400
dotnet run --project interop/AS24Net.Interop -- cases              # the combinations tested
dotnet run --project interop/AS24Net.Interop -- down               # stops everything and removes the data
```

The runner builds AS24Net from this repository (`AS24Net.Server/Dockerfile`), so it tests the working tree. The
first run builds the images of the other stations too and downloads OpenAS2 and Mendelson from SourceForge, which
takes a few minutes; later runs start in about half a minute. Docker (Compose v2) and the .NET SDK are needed,
nothing else.

## What happens

1. `interop/work/certs` gets a self-signed key pair per station (created once, valid for ten years).
2. `docker compose` starts PostgreSQL, AS24Net and the stations asked for (`docker-compose.yml`, project
   `as24net-interop`). AS24Net listens on `http://localhost:18080`.
3. The runner puts an API token and the settings the tests need (public URL, short send interval) straight into
   the database and restarts AS24Net, then sets up the rest through the REST API: an identity, and a connection and
   a partner for every station and case.
4. Every case is sent in both directions:
   - **outbound**: queued in AS24Net through the REST API; passes when AS24Net reports it `Delivered` with a
     positive MDN (signature and MIC checked) and the station stored exactly the bytes that were sent;
   - **inbound**: sent by the station; passes when AS24Net received exactly those bytes, saw the layers the case
     has (signed, encrypted, compressed) and the station got a positive MDN from AS24Net.
5. The result is printed and written to `interop/work/report.md`; the exit code is 1 when a case failed.

The payload is EDIFACT-like text with CRLF and lone LF line ends, non-ASCII characters and all 256 byte values, so
that any change on the way is noticed.

## Cases

| Case | Signature | Encryption | Compression | MDN |
|---|---|---|---|---|
| `plain` | – | – | – | synchronous, unsigned |
| `signed` | SHA-256 | – | – | synchronous, signed |
| `encrypted` | – | AES-256 | – | synchronous, unsigned |
| `sha1-aes256`, `sha256-aes256`, `sha384-aes256`, `sha512-aes256` | SHA-1 … SHA-512 | AES-256 | – | synchronous, signed |
| `sha256-aes128`, `sha256-aes192`, `sha256-3des` | SHA-256 | AES-128, AES-192, 3DES | – | synchronous, signed |
| `compressed` | SHA-256 | AES-256 | before signing | synchronous, signed |
| `compressed-after-signing` | SHA-256 | AES-256 | after signing | synchronous, signed |
| `async-mdn`, `async-unsigned-mdn` | SHA-256 | AES-256 | – | asynchronous, signed / unsigned |
| `no-mdn` | SHA-256 | AES-256 | – | none |

AS24Net requires of the partner what the case sends (*Must be signed*, *Must be encrypted*), so a layer missing on
the way makes the case fail.

## Stations

| Station | How it runs | Configured by | Not tested |
|---|---|---|---|
| [pyas2lib](https://github.com/abhishek-ram/pyas2lib) 1.4.4 | `pyas2/server.py`: an AS2 endpoint and a small control API around the library | nothing to configure: it takes any AS2 name and the options with every message | inbound `compressed-after-signing` (pyas2lib compresses before signing only) |
| [OpenAS2](https://github.com/OpenAS2/OpenAs2App) 4.12.0 | the release on Java 21 (`openas2/`) | `partnerships.xml` written by the runner, keystore built from the test certificates | – |
| [Mendelson opensource AS2](https://mendelson-e-c.com/as2) 1.1b69 | the release on Java 21 started by `mendelson/InteropLauncher.java` without its GUI | the launcher creates the stations through Mendelson's own classes from `stations.tsv` written by the runner | inbound `compressed-after-signing` and `no-mdn` (Mendelson compresses before signing only and always asks for an MDN) |

Things worth knowing about the stations:

- pyas2lib flattens every payload that is not `application/octet-stream` with Python's e-mail generator, which
  turns every LF into CRLF. The pyas2lib station therefore sends `application/octet-stream`; with an EDI type the
  payload of the test would arrive changed (by pyas2lib, before AS24Net sees it).
- OpenAS2 sends an unsecured message without `Content-Disposition`, so AS24Net stores it under a name made of the
  Message-ID.
- Mendelson has no headless mode and no API to create partners. `InteropLauncher` starts its server class directly
  with `java.awt.headless=true`, which Mendelson does not document; a new Mendelson build may need the launcher
  adapted. Mendelson keeps the security settings with the remote partner and every AS2 name once, so each case has
  its own pair of AS2 names: `MENDELSON-<case>` and an identity `AS24NET-<case>` in AS24Net.
- Mendelson sends an unsigned, uncompressed message without MIME: the encrypted content is the payload itself, not
  a MIME entity (`AS2MessageCreation.createMessageNoMIME`), and it reads such a message the same way when it
  receives one (`AS2MessageParser.writePayloadsToMessage`). pyas2lib and OpenAS2 put a MIME entity into the
  envelope, as S/MIME describes. AS24Net reads both forms, and sends Mendelson's with the connection setting
  *Without MIME when not signed*, which the runner sets for the Mendelson partners.

## Results

On a MacBook (Apple Silicon, Docker Desktop), 2026-09-24: 87 of 90 cases passed; the 3 skipped are the
combinations a station cannot send (see above).

| Load test | Messages | Throughput | Latency p50 / p99 |
|---|---|---|---|
| inbound, 32 connections, 10 KiB, `sha256-aes256` | 2,000, no error | 77 messages/s | 390 / 713 ms |
| outbound, 10 KiB, `sha256-aes256` | 2,000, all delivered | 54 messages/s (API: 295 messages/s queued) | – |

## Load test

`load inbound` posts messages to `/as2` from `--concurrency` connections at once, each built by `AS24Net.Core` and
secured as the case says (default `sha256-aes256`: signed, encrypted, synchronous signed MDN), and verifies every MDN:
its signature, the disposition and the MIC. It prints the throughput and the latency (p50, p90, p99, max).

`load outbound` queues messages through the REST API (as an application would), and receives them itself on the
host (port 18099) as the partner `LOAD`, opening every message and answering with a synchronous MDN. It measures the
time from the first message queued to the last one delivered.

Both run against the same Docker environment (a container of AS24Net with PostgreSQL next to it on the same machine),
so the numbers compare builds and settings rather than predict a production server. `--size` sets the payload
(random bytes, default 10 KiB) and `--case` any of the cases with a synchronous MDN or none.
