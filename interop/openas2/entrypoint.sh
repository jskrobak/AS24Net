#!/bin/sh
# Prepares OpenAS2 for the tests: the keystore from the test certificates, the partnerships written by the test
# runner and the properties that differ from config.xml, then starts the server in the foreground.
set -e
cd /opt/openas2
config=/opt/openas2/config

rm -f "$config/as2_certs.p12"
openssl pkcs12 -export -in /certs/openas2.pem -inkey /certs/openas2.key -name openas2 \
    -out /tmp/openas2.p12 -passout pass:interop
keytool -importkeystore -noprompt -srckeystore /tmp/openas2.p12 -srcstoretype PKCS12 -srcstorepass interop \
    -destkeystore "$config/as2_certs.p12" -deststoretype PKCS12 -deststorepass interop
keytool -importcert -noprompt -alias as24net -file /certs/as24net.pem \
    -keystore "$config/as2_certs.p12" -storetype PKCS12 -storepass interop

cp /partnerships/partnerships.xml "$config/partnerships.xml"

cat > "$config/openas2.properties" <<PROPERTIES
as2_keystore_password=interop
as2_async_mdn_url=http://openas2:10081
storageBaseDir=/data
partnerships.polling.interval=5
pollerConfigBase.interval=1
processor.resend_max_retries=1
PROPERTIES
export OPENAS2_PROPERTIES_FILE="$config/openas2.properties"

exec /opt/openas2/bin/start-openas2.sh
