# Recipes for the manual test of the server backend: two server nodes in docker-compose and two Clients.
# Run `just checklist` for the test steps. Start each Client in its own terminal.

# The same variable sets the key of the docker-compose nodes.
api_key := env("TUIGETHER_API_KEY", "dev-api-key")

# List the recipes.
default:
    @just --list --unsorted

# Print the key checklist of the current ticket.
checklist:
    #!/usr/bin/env bash
    cat <<'EOF'
    Server backend: Session list (ticket 05)

    Terminal 1: just up
    Terminal 2: just a          (Client A on node A, user ann)
    Terminal 3: just b ann      (Client B on node B, same user, so B may delete)

     1. Client A: n, type a title, Enter.     -> The Session shows on Client B.
     2. Client B: x on that Session.          -> The Session goes away on Client A.
     3. Terminal 1: just stop-node a          -> Client A shows "server: connection lost, retrying…".
     4. Terminal 1: just start-node a         -> Client A shows "server: connected" and the full list.
     5. Quit A (q). Run: just wrong-key        -> A notification and the footer show the API key reason.
     6. Quit. Run: just missing-url           -> The startup message names serverUrl (TUIGETHER_SERVER_URL).
     7. Run: just firebase                    -> Firebase mode looks as before. The footer shows no server state.

    Afterwards: just down
    EOF

# Build the F# client.
build:
    dotnet build src

# Start PostgreSQL and both nodes, and wait until both nodes answer Healthy.
up:
    #!/usr/bin/env bash
    set -euo pipefail
    docker compose up --build --detach
    for port in 5101 5102; do
      for _ in $(seq 1 60); do
        curl -fs "localhost:$port/health" | grep -q Healthy && break
        sleep 1
      done
      echo "node on port $port: $(curl -fs "localhost:$port/health" || echo unreachable)"
    done

# Stop the nodes and delete the database.
down:
    docker compose down

# Delete the database and start again with an empty Session list.
reset: down up

# Delete all data in the database and start both nodes on an empty database. Seq keeps its logs.
wipe: && up
    #!/usr/bin/env bash
    set -euo pipefail
    docker compose stop node-a node-b
    docker compose up --detach --wait postgres
    docker compose exec -T postgres psql --username=tuigether --dbname=postgres \
      --command="drop database if exists tuigether with (force)" \
      --command="create database tuigether"

# Show the health of both nodes.
health:
    @echo "node a: $(curl -fs localhost:5101/health || echo unreachable)"
    @echo "node b: $(curl -fs localhost:5102/health || echo unreachable)"

# Stop one node (a or b). Its Clients lose the connection.
stop-node node:
    docker compose stop node-{{ node }}

# Start one node (a or b) again. Its Clients reconnect.
start-node node:
    docker compose start node-{{ node }}

# Client A on node A.
a user="ann": build
    @just _client a {{ user }} {{ api_key }}

# Client B on node B.
b user="bob": build
    @just _client b {{ user }} {{ api_key }}

# Client A with a wrong API key.
wrong-key: build
    @just _client a ann wrong-api-key

# The client with backend server and no serverUrl. It reads an empty config, not your own.
missing-url: build
    #!/usr/bin/env bash
    set -uo pipefail
    config=$(mktemp -d)
    mkdir -p "$config/tuigether"
    echo '{}' > "$config/tuigether/config.json"
    XDG_CONFIG_HOME="$config" \
    TUIGETHER_BACKEND=server \
    TUIGETHER_SERVER_URL= \
    TUIGETHER_SERVER_API_KEY={{ api_key }} \
    TUIGETHER_USER=ann \
    dotnet run --project src --no-build
    rm -rf "$config"

# The client in Firebase mode, with the Firebase settings of your own config.
firebase: build
    TUIGETHER_BACKEND=firebase TUIGETHER_LOG_DIR=logs/firebase dotnet run --project src --no-build

# Follow the log of one Client (a, b or firebase).
log client:
    tail -f logs/{{ client }}/tuigether-$(date +%F).log

# Mutation run of the server specs with Stryker.NET. Needs Docker. Extra arguments go to Stryker, e.g. --mutate "**/DrivingRules.cs".
[positional-arguments]
[working-directory('server/Tuigether.Server.Specs')]
mutate *args:
    dotnet tool restore
    dotnet stryker "$@"

# The Bobcat UI on port 5525: live spec runs and the Event Model, in the browser. Extra arguments go to `bobcat ui`, e.g. --no-browser.
[positional-arguments]
bobcat-ui *args:
    dotnet tool restore
    dotnet bobcat ui "$@"

_client node user key:
    TUIGETHER_BACKEND=server \
    TUIGETHER_SERVER_URL=http://localhost:{{ if node == "a" { "5101" } else { "5102" } }} \
    TUIGETHER_SERVER_API_KEY={{ key }} \
    TUIGETHER_USER={{ user }} \
    TUIGETHER_LOG_DIR=logs/{{ node }} \
    dotnet run --project src --no-build
