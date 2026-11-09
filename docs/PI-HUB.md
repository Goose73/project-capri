# Capri Hub — Raspberry Pi foundation (experimental)

This is a small **Linux ARM64 CLI and monitoring process** intended for a Raspberry Pi 4.
It is **not** a deployed Capri service, a remote-control API, or the M5 Windows task authority.
The software has no inbound listener, no AI inference, no secrets, and no access to the
Windows task executor. It can check a LAN PC over ICMP and optionally dispatch one
Wake-on-LAN packet from a local command.

## Why this form?

The target Pi 4 Model B has 2 GB RAM, Debian 13 ARM64, and already runs
Home Assistant, Uptime Kuma, Pi-hole, Tailscale and separate local Python services.
A self-contained .NET CLI avoids modifying those services or binding an occupied port.
It should not be deployed automatically or assume exclusive ownership of the machine.

**Known limits:** ICMP alone may report a powered-on PC as offline when a firewall
blocks ping. If the platform or container lacks permission to probe, status should
be treated as **unknown**, not proof the PC is switched off. A successful Wake-on-LAN
UDP send only proves the packet was dispatched; it does not prove Windows booted.
Wake-on-LAN cannot start a PC with mains power physically switched off.

## Build and test (on a development machine)

```sh
dotnet test tests/Capri.Hub.Tests/Capri.Hub.Tests.csproj -c Release
dotnet publish src/Capri.Hub/Capri.Hub.csproj -c Release \
  -r linux-arm64 --self-contained true -p:PublishSingleFile=true \
  -p:PublishTrimmed=false -o out/pi-hub
```

The GitHub Actions **Raspberry Pi Hub CI** workflow runs Linux tests and publishes
a self-contained `capri-hub-linux-arm64` artifact. Build/test on a GitHub runner
is **not** an actual Raspberry Pi acceptance test.

## Optional manual Pi usage (not performed remotely)

Download the ARM64 artifact from the successful GitHub Actions run, transfer it
to a folder owned by the Pi user, and extract it there. In that folder:

```sh
chmod 700 ./capri-hub
export CAPRI_PC_IP="192.168.x.x"            # your PC's actual private LAN IP
./capri-hub doctor
./capri-hub status --json
./capri-hub watch
```

Stop `watch` with Ctrl+C. It emits one JSON line per poll (60 seconds by default)
and does not create any configuration files. To change cadence set
`CAPRI_POLL_SECONDS` to 10–3600.

**Optional** manual wake packet (only if you deliberately want an additional wake
method, and only after confirming the existing one works):

```sh
export CAPRI_WOL_MAC="AA-BB-CC-DD-EE-FF"       # real wired NIC MAC, locally only
export CAPRI_WOL_BROADCAST="192.168.x.255"     # correct LAN broadcast
./capri-hub wake --confirm
```

Neither `status` nor `watch` sends a wake packet. A `wake` invocation without
`--confirm` refuses. No endpoint exists for remote callers to invoke `wake`.
No addresses, credentials or MACs are committed to the repository.

The best next step is to integrate a **read-only** Pi presence signal with
the future Windows M5 task authority over an authenticated, explicitly designed
private channel. Do **not** expose the Windows task executor or approval endpoint
over HTTP or Tailscale Serve as part of this first slice.

## Existing deployment boundaries

Do not replace, restart or reconfigure Home Assistant, Uptime Kuma, Pi-hole,
Tailscale, Docker networks, the working remote Wake-on-LAN implementation, or
existing local servers on ports 80, 8765, 8787, 3001 and 8124.
This foundation does not need any inbound port, Docker container, systemd unit
or privileged installation.

## Next acceptance milestones

1. Verify on real Pi: resource usage, ICMP permissions and online/offline transitions.
2. Verify manual wake dispatch **only if requested** (PC receiving mains power).
3. Build a secure Windows/Pi read-only status bridge without ambient command execution.
4. Add reconnection, health reporting and optional service installation after review.
5. Keep M4 supervised task approval and full M5 IPC acceptance independent.
