# TrueNAS SCALE deployment

This guide covers a catalog Jellyfin app with persistent host-path storage for
`/config`. Names and paths differ between installations; substitute the values
from your own TrueNAS configuration.

## 1. Run node-csfd-api as a Custom App

Open **Apps → Discover**, use the top-right menu, and select
**Install via YAML**. Set the application name to `csfd-api` and paste the
contents of [`compose.yaml`](compose.yaml).

The example publishes container port `3000` as host port `3030`. If TrueNAS has
multiple interfaces, bind the port to its LAN address by changing:

```yaml
ports:
  - "3030:3000"
```

to:

```yaml
ports:
  - "YOUR_TRUENAS_LAN_IP:3030:3000"
```

Do not forward this port on your router or expose it through a public reverse
proxy.

Verify the service:

```sh
curl -fsS http://YOUR_TRUENAS_LAN_IP:3030/movie/8852
```

## 2. Install web injection dependencies

In Jellyfin, open **Dashboard → Plugins → Repositories** and add:

```text
https://raw.githubusercontent.com/n00bcodr/jellyfin-plugins/main/10.11/manifest.json
```

Install **JavaScript Injector** and restart Jellyfin. Installing
**File Transformation** is recommended because it avoids direct changes to the
Jellyfin Web `index.html` file.

## 3. Copy the plugin

Find the host path mapped to Jellyfin's `/config`. In this example it is
`/mnt/POOL/apps/jellyfin`. Replace both the path and ZIP version as required:

```sh
install -d -o 568 -g 568 -m 755 '/mnt/POOL/apps/jellyfin/plugins/Csfd Badge'
unzip -o '/tmp/Jellyfin.Plugin.CsfdBadge_VERSION.zip' -d '/mnt/POOL/apps/jellyfin/plugins/Csfd Badge'
chown -R 568:568 '/mnt/POOL/apps/jellyfin/plugins/Csfd Badge'
```

UID/GID `568:568` is the default `apps` account. Use the values configured for
your Jellyfin app if they differ.

Restart Jellyfin from the TrueNAS Apps screen.

## 4. Configure ČSFD Badge

Open **Jellyfin Dashboard → Plugins → ČSFD Badge** and set:

```text
http://YOUR_TRUENAS_LAN_IP:3030
```

The catalog Jellyfin app and the Custom App normally use separate Docker
networks, so `localhost` and the Compose service name usually do not work.

The web script registers automatically during startup. If JavaScript Injector
was installed later, open **Dashboard → Scheduled Tasks** and run
**Register ČSFD web badge** manually.
