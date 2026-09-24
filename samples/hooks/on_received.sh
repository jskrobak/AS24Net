#!/bin/sh
# Example hook: copies every received payload to an inbox directory named after the partner
# and logs the event. Configure it with Hooks__OnReceived=/path/to/on_received.sh
# All parameters are available as AS2_* environment variables and as JSON on standard input.
set -eu

inbox="${INBOX_DIR:-/data/inbox}/${AS2_PARTNER_AS2_ID}"
mkdir -p "$inbox"
cp "$AS2_FILE_PATH" "$inbox/${AS2_RECEIVED_MESSAGE_ID}_${AS2_FILE_NAME}"

# Standard output is written to the application log and to the transfer log.
echo "Copied $AS2_FILE_NAME ($AS2_SIZE bytes, $AS2_CONTENT_TYPE) from $AS2_PARTNER_NAME to $inbox"
