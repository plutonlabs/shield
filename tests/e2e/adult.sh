#!/usr/bin/env bash
# End-to-end test of the adult-content filter (and of the per-program exceptions) on Linux: the real PlutonShield proxy, a local test origin, curl. See README.md.
# It checks the block page, SafeSearch (Google, Bing, DuckDuckGo, YouTube) and the cases around them (filter off, SafeSearch
# off, HTTPS filtering off, an excluded site). Nothing leaves 127.0.0.1.
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
WORK=${E2E_WORK:-$(mktemp -d)}
PORT=${E2E_PROXY_PORT:-18890}
: "${NEAT_ADBLOCK_SO:?set NEAT_ADBLOCK_SO to the path of libneat_adblock.so (cargo build --release in plutonlabs/neat-adblock)}"

HOSTS="adult.test www.google.com www.bing.com duckduckgo.com www.youtube.com ok.test"
ADDED_HOSTS=0
PIDS=()
cleanup() {
  for p in "${PIDS[@]:-}"; do [ -n "$p" ] && kill "$p" 2>/dev/null || true; done
  if [ "$ADDED_HOSTS" = 1 ]; then sed -i '/# plutonshield-e2e-adult$/d' /etc/hosts || true; fi
}
trap cleanup EXIT

echo "== host names (temporarily: $HOSTS point to 127.0.0.1)"
[ -w /etc/hosts ] || { echo "Cannot write /etc/hosts: run as root."; exit 2; }
echo "127.0.0.1 $HOSTS # plutonshield-e2e-adult" >> /etc/hosts
ADDED_HOSTS=1

echo "== test certificates"
CERTS=$WORK/certs; mkdir -p "$CERTS"; cd "$CERTS"
openssl req -x509 -newkey rsa:2048 -nodes -keyout testca.key -out testca.pem -days 30 -subj "/CN=PlutonShield e2e origin CA" 2>/dev/null
for h in $HOSTS; do
  openssl req -newkey rsa:2048 -nodes -keyout "$h.key" -out "$h.csr" -subj "/CN=$h" 2>/dev/null
  printf "subjectAltName=DNS:%s\nbasicConstraints=CA:FALSE\n" "$h" > "$h.ext"
  openssl x509 -req -in "$h.csr" -CA testca.pem -CAkey testca.key -CAcreateserial -out "$h.pem" -days 30 -extfile "$h.ext" 2>/dev/null
done
export SSL_CERT_FILE=$CERTS/testca.pem   # the proxy checks the certificates of the sites it talks to

if [ -n "${E2E_CLI_DIR:-}" ]; then
  echo "== use the command-line app in $E2E_CLI_DIR"
  cp -r "$E2E_CLI_DIR" "$WORK/cli"
else
  echo "== build the command-line app"
  dotnet build "$ROOT/src/PlutonShield.Cli" -c Release -o "$WORK/cli" -nologo -v q
fi
cp "$NEAT_ADBLOCK_SO" "$WORK/cli/libneat_adblock.so"
NS="$WORK/cli/plutonshield"

mkdir -p "$WORK/base"
if [ -n "${E2E_FILTERS_DATA:-}" ]; then
  cp -r "$E2E_FILTERS_DATA/filters" "$WORK/base/filters"
else
  echo "== download the filters"
  PLUTONSHIELD_DATA=$WORK/base "$NS" update
fi

echo "== start origin"
(E2E_CERTS=$CERTS node "$HERE/adult-origin.js" > "$WORK/origin.log" 2>&1) & PIDS+=($!)
sleep 1

# The list the filter would have downloaded, saved the way the app saves it. FiltersBaseUrl points nowhere, so the app
# cannot replace it with the real list from GitHub (it would, and adult.test is not in that one).
LIST=$WORK/optional-adult.txt
printf '# test list\nadult.test\nsome-adult.example\n' > "$LIST"
SHA=$(sha256sum "$LIST" | cut -d' ' -f1 | tr a-f A-F)

fail=0
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1 (got '$2', wanted '$3')"; fail=1; fi; }

# scenario <name> <settings json fields>: a fresh data folder, the proxy started with it, then the checks in $CHECKS
scenario() {
  local name=$1 extra=${2:-}
  local d=$WORK/$name; mkdir -p "$d/adult"; ln -s "$WORK/base/filters" "$d/filters"
  cp "$LIST" "$d/adult/optional-adult.txt"
  printf '{"File":"optional-adult.txt","Sha256":"%s","Version":"e2e","Domains":2}' "$SHA" > "$d/adult/adult.json"
  printf '{"Port":%s,"Language":"en","AutoUpdate":false,"FiltersBaseUrl":"https://127.0.0.1:9/","AdultFilter":true,"ForceSafeSearch":true%s}' "$PORT" "${extra:+,$extra}" > "$d/settings.json"
  PLUTONSHIELD_DATA=$d "$NS" run --no-system-proxy > "$d/proxy.log" 2>&1 & PROXY=$!
  PIDS+=($PROXY)
  for _ in $(seq 1 60); do grep -q "Protection is ON" "$d/proxy.log" 2>/dev/null && break; sleep 0.5; done
  grep -q "Protection is ON" "$d/proxy.log" || { echo "The proxy did not start:"; cat "$d/proxy.log"; exit 1; }
  echo "== $name"
}
# SIGTERM, not SIGINT: a background job of a script ignores SIGINT.
stop() {
  kill "$PROXY" 2>/dev/null || true
  for _ in $(seq 1 20); do kill -0 "$PROXY" 2>/dev/null || break; sleep 0.25; done
  kill -9 "$PROXY" 2>/dev/null || true
}

# get <url> [curl args]: prints "<status> <text we care about>"
get() {
  local url=$1; shift
  local out; out=$(curl -sk -m 20 --proxy "http://127.0.0.1:$PORT" -o "$WORK/body" -w '%{http_code}' -H 'Sec-Fetch-Mode: navigate' "$@" "$url" 2>/dev/null || true)
  local what; what=$(grep -o '<h1>[^<]*</h1>\|"url":"[^"]*"\|"restrict":[^}]*' "$WORK/body" 2>/dev/null | tr '\n' ' ' | sed 's/<[/]*h1>//g')
  echo "$out ${what% }"
}

scenario on
check "adult site (https) gets the block page"        "$(get https://adult.test:8443/watch?q=x)"  "403 Adult website blocked"
check "adult site (plain http) gets the block page"   "$(get http://adult.test:8080/x)"           "403 Adult website blocked"
check "a subdomain of an adult site is blocked"       "$(get https://www.adult.test:8443/)"       "403 Adult website blocked"
check "an ordinary site is not touched"               "$(get https://ok.test:8443/hello)"         '200 "url":"/hello" "restrict":null'
check "an image of an adult site is answered empty"   "$(get https://adult.test:8443/p.png -H 'Sec-Fetch-Dest: image')" "200 "
check "Google: safe=active is added"                  "$(get 'https://www.google.com:8443/search?q=cats')"            '200 "url":"/search?q=cats&safe=active" "restrict":null'
check "Google: safe=off is replaced"                  "$(get 'https://www.google.com:8443/search?q=cats&safe=off')"   '200 "url":"/search?q=cats&safe=active" "restrict":null'
check "Google: other pages are left alone"            "$(get 'https://www.google.com:8443/maps?q=cats')"              '200 "url":"/maps?q=cats" "restrict":null'
check "Google over plain http"                        "$(get 'http://www.google.com:8080/search?q=cats')"             '200 "url":"/search?q=cats&safe=active" "restrict":null'
check "Bing: adlt=strict is added"                    "$(get 'https://www.bing.com:8443/search?q=cats')"              '200 "url":"/search?q=cats&adlt=strict" "restrict":null'
check "DuckDuckGo: kp=1 is added"                     "$(get 'https://duckduckgo.com:8443/?q=cats')"                  '200 "url":"/?q=cats&kp=1" "restrict":null'
check "YouTube: the Restrict header is sent"          "$(get 'https://www.youtube.com:8443/watch?v=1')"               '200 "url":"/watch?v=1" "restrict":"Moderate"'
stop

scenario safesearch-off '"ForceSafeSearch":false'
check "SafeSearch off: Google is left alone"          "$(get 'https://www.google.com:8443/search?q=cats')"            '200 "url":"/search?q=cats" "restrict":null'
check "SafeSearch off: adult sites are still blocked" "$(get https://adult.test:8443/)"                               "403 Adult website blocked"
stop

scenario filter-off '"AdultFilter":false'
check "filter off: the adult site opens"              "$(get https://adult.test:8443/)"                               '200 "url":"/" "restrict":null'
check "filter off: no SafeSearch either"              "$(get 'https://www.google.com:8443/search?q=cats')"            '200 "url":"/search?q=cats" "restrict":null'
stop

scenario no-https '"FilterHttps":false'
check "HTTPS filtering off: https to an adult site is refused" "$(get https://adult.test:8443/ | cut -c1-3)"          "000"
check "HTTPS filtering off: plain http still gets the page"    "$(get http://adult.test:8080/)"                       "403 Adult website blocked"
stop

scenario excluded '"ExcludedHosts":["adult.test"]'
check "excluded site: the adult filter leaves it alone" "$(get https://adult.test:8443/)"                             '200 "url":"/" "restrict":null'
stop

# Per-program exceptions: the program here is curl (found by its process id, matched on its file name).
# get_strict checks the certificate (no -k): a connection that was opened up by the proxy would carry the proxy's own
# certificate and fail, one that went through untouched carries the origin's (signed by the test CA).
get_strict() { curl -s -m 20 --cacert "$CERTS/testca.pem" --proxy "http://127.0.0.1:$PORT" -o "$WORK/body" -w '%{http_code}' -H 'Sec-Fetch-Mode: navigate' "$1" 2>/dev/null || true; }

scenario program-left-alone '"SkippedApps":[{"Exe":"curl","Path":"/usr/bin/curl"}]'
check "program left alone: an adult site is not blocked"         "$(get https://adult.test:8443/)"                    '200 "url":"/" "restrict":null'
check "program left alone: plain http is not blocked either"     "$(get http://adult.test:8080/)"                     '200 "url":"/" "restrict":null'
check "program left alone: no SafeSearch is added"               "$(get 'https://www.google.com:8443/search?q=cats')" '200 "url":"/search?q=cats" "restrict":null'
check "program left alone: HTTPS is not opened (origin certificate arrives untouched)" "$(get_strict https://ok.test:8443/hello)" "200"
stop

scenario other-program-left-alone '"SkippedApps":[{"Exe":"wget","Path":"/usr/bin/wget"}]'
check "another program left alone: curl is still blocked"        "$(get https://adult.test:8443/)"                    "403 Adult website blocked"
check "another program left alone: HTTPS is still opened up (the origin certificate is not what arrives)" "$(get_strict https://ok.test:8443/hello | cut -c1-3)" "000"
stop

[ "$fail" = 0 ] && echo "ALL OK" || { echo "FAILED"; exit 1; }
