#!/usr/bin/env bash
# Throwaway Jellyfin 12 for the chapter-marker E2E. Never touches a real server or the NAS.
#
#   e2e_jellyfin.sh media     test videos from corpus audio (real show audio, 1 fps black picture, no chapters)
#   e2e_jellyfin.sh plugin    Release build of this checkout into the server's plugin folder
#   e2e_jellyfin.sh start     start the server in the background (port 8196, all interfaces)
#   e2e_jellyfin.sh setup     first-run wizard, admin user, Phish library, chapter markers on
#   e2e_jellyfin.sh stop
#
# Needs: the portable Jellyfin unpacked at $E2E/server/jellyfin (repo.jellyfin.org, portable/any),
# the .NET 10 runtime ($DOTNET), ffmpeg on PATH, and the corpus from tools/make_corpus.py ($PHISHDATA_CORPUS).
set -euo pipefail

E2E=${E2E:-$HOME/.cache/phishdata-e2e}
DOTNET=${DOTNET:-$HOME/.dotnet10/dotnet}
CORPUS=${PHISHDATA_CORPUS:-$HOME/.cache/phishdata-corpus/shows}
PORT=${PORT:-8196}
URL=http://localhost:$PORT
REPO=$(cd "$(dirname "$0")/.." && pwd)
SERVER=$E2E/server/jellyfin
MEDIA=$E2E/media/Phish
ADMIN=e2e
PASS=e2e-only

audio() { ls "$CORPUS"/"$1"_*/audio.mka | head -1; }

# A video Jellyfin probes like a show: real audio, a 1 fps black picture, and no chapters.
make_video() { # <audio> <out> [seconds]
    [[ -f $2 ]] && return
    ffmpeg -nostdin -v error -f lavfi -i color=c=black:s=320x180:r=1 -i "$1" ${3:+-t "$3"} \
        -map 0:v -map 1:a -map_chapters -1 -map_metadata -1 -c:v libx264 -preset ultrafast -tune stillimage \
        -c:a copy -shortest "$2.part.mkv"
    mv "$2.part.mkv" "$2"
}

token() { cat "$E2E/token"; }
api() { # <method> <path> [json]
    curl -sf -X "$1" "$URL$2" -H "Authorization: MediaBrowser Client=\"e2e\", Device=\"cli\", DeviceId=\"e2e\", Version=\"1\", Token=\"$(token 2>/dev/null)\"" \
        -H 'Content-Type: application/json' ${3:+-d "$3"}
}

case ${1:-} in
media)
    mkdir -p "$MEDIA"
    make_video "$(audio 2026-07-07)" "$MEDIA/ph2026-07-07.Webcast.UntouchedTrimmed.1080p.hetyet.mkv"   # aligns
    make_video "$(audio 2025-06-27)" "$MEDIA/ph2025-06-27.mkv"                                          # no-consensus -> Needs review
    make_video "$(audio 2023-07-16)" "$MEDIA/ph2026-07-09.Webcast.mkv"                                  # not on phish.in -> Pending
    make_video "$(audio 2026-07-07)" "$MEDIA/Phish-2024-07-11.Evolve-The.Tonight.Show.1080p.mkv" 345    # 5:45 clip -> too-short
    ls -la "$MEDIA"
    ;;
plugin)
    dir=$E2E/data/plugins/PhishData_e2e
    rm -rf "$dir" && mkdir -p "$dir"
    "$DOTNET" build "$REPO/Jellyfin.Plugin.PhishNet" -c Release -o "$E2E/build" -v q -nologo
    cp "$E2E/build/Jellyfin.Plugin.PhishNet.dll" "$E2E/build/Jellyfin.Plugin.PhishNet.pdb" "$dir/"
    ls -la "$dir"
    ;;
start)
    mkdir -p "$E2E/config" "$E2E/log"
    [[ -f $E2E/config/network.xml ]] || cat > "$E2E/config/network.xml" <<XML
<?xml version="1.0" encoding="utf-8"?>
<NetworkConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <InternalHttpPort>$PORT</InternalHttpPort>
  <PublicHttpPort>$PORT</PublicHttpPort>
  <AutoDiscovery>false</AutoDiscovery>
</NetworkConfiguration>
XML
    nohup "$DOTNET" "$SERVER/jellyfin.dll" --datadir "$E2E/data" --configdir "$E2E/config" --cachedir "$E2E/cache" \
        --logdir "$E2E/log" --webdir "$SERVER/jellyfin-web" --ffmpeg "$(command -v ffmpeg)" > "$E2E/server.out" 2>&1 &
    echo $! > "$E2E/server.pid"
    for _ in $(seq 60); do curl -sf "$URL/System/Info/Public" >/dev/null && break; sleep 1; done
    curl -sf "$URL/System/Info/Public"; echo
    ;;
setup)
    api POST /Startup/Configuration '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
    api GET /Startup/User >/dev/null
    api POST /Startup/User "{\"Name\":\"$ADMIN\",\"Password\":\"$PASS\"}"
    api POST /Startup/Complete
    curl -sf "$URL/Users/AuthenticateByName" -H 'Content-Type: application/json' \
        -H 'Authorization: MediaBrowser Client="e2e", Device="cli", DeviceId="e2e", Version="1"' \
        -d "{\"Username\":\"$ADMIN\",\"Pw\":\"$PASS\"}" | python3 -c 'import json,sys; print(json.load(sys.stdin)["AccessToken"])' > "$E2E/token"
    api POST "/Library/VirtualFolders?name=Phish&collectionType=movies&refreshLibrary=false" \
        "{\"LibraryOptions\":{\"PathInfos\":[{\"Path\":\"$MEDIA\"}]}}"
    id=$(api GET /Plugins | python3 -c 'import json,sys; print(next(p["Id"] for p in json.load(sys.stdin) if "Phish" in p["Name"]))')
    api GET "/Plugins/$id/Configuration" | python3 -c 'import json,sys; c=json.load(sys.stdin); c["EnableChapterMarkers"]=True; print(json.dumps(c))' > "$E2E/plugin-config.json"
    api POST "/Plugins/$id/Configuration" "$(cat "$E2E/plugin-config.json")"
    echo "setup done; plugin $id"
    ;;
stop)
    [[ -f $E2E/server.pid ]] && kill "$(cat "$E2E/server.pid")" && rm "$E2E/server.pid"
    ;;
*)
    sed -n '2,12p' "$0"; exit 2
    ;;
esac
