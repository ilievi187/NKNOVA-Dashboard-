#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "This isolated Secret Service runner is for Linux only." >&2
  exit 2
fi

for tool in dbus-run-session gnome-keyring-daemon secret-tool; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Missing $tool. Install dbus-daemon, gnome-keyring, and libsecret-tools, then retry." >&2
    exit 2
  fi
done

temp_base="${RUNNER_TEMP:-${JCODE_SCRATCH_DIR:-${TMPDIR:-/tmp}}}"
mkdir -p "$temp_base"
test_home="$(mktemp -d "$temp_base/nknova-secret-service.XXXXXX")"
chmod 700 "$test_home"
export NKNOVA_SECRET_SERVICE_TEST_HOME="$test_home"

dbus-run-session -- bash -euo pipefail -c '
  export HOME="$NKNOVA_SECRET_SERVICE_TEST_HOME"
  export XDG_RUNTIME_DIR="$HOME/.run"
  export XDG_CONFIG_HOME="$HOME/.config"
  export XDG_DATA_HOME="$HOME/.local/share"
  export XDG_CACHE_HOME="$HOME/.cache"
  mkdir -p "$XDG_RUNTIME_DIR" "$XDG_CONFIG_HOME" "$XDG_DATA_HOME" "$XDG_CACHE_HOME"
  chmod 700 "$XDG_RUNTIME_DIR"

  keyring_password="$(head -c 32 /dev/urandom | base64)"
  login_environment="$(printf %s "$keyring_password" | gnome-keyring-daemon --login --components=secrets)"
  unset keyring_password
  eval "$login_environment"
  start_environment="$(gnome-keyring-daemon --start --components=secrets)"
  eval "$start_environment"

  dotnet run --project Nolvus.Services.Tests/Nolvus.Services.Tests.csproj --configuration Release
'
