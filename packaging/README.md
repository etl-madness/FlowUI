# FlowUI Packaging & OS Service Guide (`/opt/flow-ui` & Windows WinGet / SCM)

This directory contains build scripts, package metadata, OS service definitions (`systemd`, OpenRC, SysVinit, FreeBSD `rc.d`, OpenBSD `rc.d`, macOS `launchd`, and native Windows Service scripts), `man` pages, GNU `info` documentation, WiX `.msi` manifests, and WinGet YAML manifests for packaging **ETL Madness FlowUI (Flow Builder Studio)** across Linux (`.deb`, `.rpm` for RHEL/Fedora/SUSE/openSUSE, and `.pkg.tar.zst` for Arch Linux), macOS (`.pkg`), FreeBSD (`.pkg`), OpenBSD (`.tgz`), and Windows (`.zip` & `.msi` for WinGet).

- **Isolated Prefixes (Zero File Conflicts with System, `go-flow`, or `go-flowAPI`)**: Unix packages install under `/opt/flow-ui`, Windows `.msi` installs under `C:\Program Files\ETL Madness\Flow UI\`, and WinGet portable `.zip` installs inside `%LOCALAPPDATA%\Microsoft\WinGet\Packages\ETLMadness.FlowUI_...`. It can be installed side-by-side with `go-flow` (`/opt/flow`) and `go-flowAPI` (`/opt/flow-api`) with zero file collisions.
- **Non-Colliding Package Metadata**: OS packages use `flow-ui` (`com.etlmadness.flow-ui` on macOS, `ETLMadness.FlowUI` on WinGet).
- **`.NET 10` Runtime Bundling**: Linux, macOS, and Windows packages bundle a self-contained `.NET 10` runtime (zero `.NET` prerequisite on the target host). FreeBSD and OpenBSD packages publish framework-dependent (`FlowUI.dll`): FreeBSD uses the official `dotnet` package (`pkg install dotnet`), while OpenBSD requires a custom/source-built `.NET 10` runtime (since the standard OpenBSD ports tree does not ship `dotnet`; alternatively, host `FlowUI` on Linux/FreeBSD/macOS/Windows pointed at your OpenBSD `go-flowAPI` backend via `FLOW_API_URL`).
- **Native OS Services Included**: Includes production-ready service definitions, unprivileged `flowui` daemon account provisioning, preserved `flow-ui.env` configuration files (`noreplace` / `conffiles`), and native Windows Service Control Manager (SCM) support built directly into `FlowUI.exe` (no NSSM required).

---

## Included Assets & Install Locations

| Asset | Linux (`.deb` / `.rpm` / Arch `.pkg.tar.zst`) | macOS (`.pkg`) | FreeBSD (`.pkg`) & OpenBSD (`.tgz`) | Windows (`.msi` / WinGet `.zip`) |
| :--- | :--- | :--- | :--- | :--- |
| **Application Bundle** | `/opt/flow-ui/app/` *(self-contained)* | `/opt/flow-ui/app/` *(self-contained)* | `/opt/flow-ui/app/` *(framework-dependent)* | `<Prefix>\app\FlowUI.exe` + assets |
| **CLI Launcher** | `/opt/flow-ui/bin/flow-ui` | `/opt/flow-ui/bin/flow-ui` | `/opt/flow-ui/bin/flow-ui` | `<Prefix>\app\FlowUI.exe` |
| **Daemon Env Config** | `/etc/flow-ui/flow-ui.env` *(0640, preserved)* | `/opt/flow-ui/etc/flow-ui.env` *(0600, preserved)* | FreeBSD: `/usr/local/etc/flow-ui/flow-ui.env`<br>OpenBSD: `/etc/flow-ui/flow-ui.env` | `C:\ProgramData\ETL Madness\Flow UI\flow-ui.env` |
| **Primary OS Service** | `/usr/lib/systemd/system/flow-ui.service` | `/opt/flow-ui/share/services/launchd/com.etlmadness.flow-ui.plist` | FreeBSD: `/usr/local/etc/rc.d/flow_ui`<br>OpenBSD: `/etc/rc.d/flow_ui` | Native SCM in `FlowUI.exe` + `<Prefix>\share\services\windows\*.ps1` |
| **Alternate `rc` Scripts** | `/opt/flow-ui/share/services/{systemd,openrc,sysvinit}/*` | — | `/opt/flow-ui/share/services/{freebsd,openbsd}/*` | — |
| **Reference Docs** | `/opt/flow-ui/share/doc/*.md` & `/opt/flow-ui/share/{man,info}/*` | `/opt/flow-ui/share/doc/*.md` & `/opt/flow-ui/share/{man,info}/*` | `/opt/flow-ui/share/doc/*.md` & `/opt/flow-ui/share/{man,info}/*` | `<Prefix>\docs\*.md` & `<Prefix>\share\man\man{1,5}\*` |
| **State & Log Dirs** | `/var/lib/flow-ui` & `/var/log/flow-ui` | `/var/db/flow-ui` & `/var/log/flow-ui` | `/var/db/flow-ui` & `/var/log/flow-ui` | `C:\ProgramData\ETL Madness\Flow UI\{data,logs}` |
| **`PATH` / `MANPATH` / `INFOPATH`** | `/etc/profile.d/flow-ui.sh` | `/private/etc/paths.d/flow-ui` & `/private/etc/manpaths.d/flow-ui` | FreeBSD: `/usr/local/etc/profile.d/flow-ui.sh` & `/usr/local/etc/man.d/flow-ui.conf`<br>OpenBSD: `/etc/profile.d/flow-ui.sh` | MSI `Part="last"` `PATH` append / WinGet `Links` |

*(On Windows, `<Prefix>` is `C:\Program Files\ETL Madness\Flow UI` for `.msi` or `%LOCALAPPDATA%\Microsoft\WinGet\Packages\ETLMadness.FlowUI_...` for portable `.zip`.)*

---

## Directory Layout

```text
packaging/
├── etc/
│   ├── flow-ui/
│   │   └── flow-ui.env                      # Default daemon environment config template
│   └── profile.d/
│       └── flow-ui.sh                       # POSIX sh login profile drop-in (PATH + INFOPATH)
├── services/
│   ├── systemd/
│   │   └── flow-ui.service                  # Linux systemd service unit
│   ├── openrc/
│   │   └── flow-ui                          # Alpine / Gentoo OpenRC init script
│   ├── sysvinit/
│   │   └── flow-ui                          # LSB POSIX SysVinit /etc/init.d script
│   ├── freebsd/
│   │   └── flow_ui                          # FreeBSD /etc/rc.subr service script
│   ├── openbsd/
│   │   └── flow_ui                          # OpenBSD /etc/rc.d/rc.subr service script
│   ├── launchd/
│   │   └── com.etlmadness.flow-ui.plist     # macOS LaunchDaemon plist
│   └── windows/
│       ├── Install-FlowUIService.ps1        # Native Windows SCM service installer/updater
│       └── Uninstall-FlowUIService.ps1      # Windows SCM service uninstaller
├── debian/
│   ├── postinst                             # Creates flowui user/dirs & reloads systemd
│   ├── prerm                                # Stops & disables service before removal
│   └── postrm                               # Reloads systemd & cleans /etc/flow-ui on purge
├── arch/
│   ├── PKGBUILD.tmpl                        # Arch Linux PKGBUILD template
│   └── flow-ui.install                      # Pacman install/upgrade/remove hooks
├── rpm/
│   └── flow-ui.spec                         # RPM spec file (RHEL, Fedora, SUSE, openSUSE)
├── macos/
│   └── Distribution.xml.tmpl                # macOS productbuild distribution template
├── freebsd/
│   ├── +MANIFEST.tmpl                       # FreeBSD pkg manifest template
│   ├── +POST_INSTALL                        # Creates flowui user/dirs & seeds env config
│   └── +PRE_DEINSTALL                       # Stops flow_ui rc.d service before removal
├── openbsd/
│   ├── +DESC                                # OpenBSD pkg_create description
│   ├── +INSTALL                             # Creates flowui user/dirs & seeds env config
│   └── +DEINSTALL                           # Stops flow_ui rc.d service before removal
├── windows/
│   ├── flow-ui.wxs                          # WiX Toolset v4/v5/v6 MSI manifest
│   └── flow-ui-wix3.wxs                     # WiX v3 / Linux msitools (wixl) MSI manifest
├── winget/
│   └── README.md                            # WinGet validation, local install & publishing guide
├── man/
│   ├── man1/flow-ui.1                       # flow-ui(1) CLI & daemon manual page
│   └── man5/flow-ui.env.5                   # flow-ui.env(5) configuration manual page
├── info/
│   ├── flow-ui.texi                         # GNU Texinfo source manual
│   ├── flow-ui.info                         # Compiled GNU Info manual (with Tag Table)
│   └── dir                                  # Self-contained GNU Info directory index
├── nfpm.yaml                                # Optional nFPM fallback config (.deb / .rpm / Arch)
└── scripts/
    ├── common.sh                            # Shared POSIX sh helpers & .NET 10 publishing
    ├── generate-docs.sh                     # Recompiles .texi -> .info if makeinfo exists
    ├── build-all.sh                         # Unified entrypoint for host or specific targets
    ├── build-deb.sh                         # Builds Linux .deb (dpkg-deb or nfpm)
    ├── build-rpm.sh                         # Builds Linux .rpm (rpmbuild or nfpm)
    ├── build-arch.sh                        # Builds Arch Linux .pkg.tar.zst (makepkg or nfpm)
    ├── build-macos-pkg.sh                   # Builds macOS .pkg (pkgbuild + productbuild)
    ├── build-freebsd-pkg.sh                 # Builds FreeBSD .pkg (pkg create)
    ├── build-openbsd-pkg.sh                 # Builds OpenBSD .tgz (pkg_create or cross-bundle)
    ├── build-windows.sh                     # Cross-builds Windows .zip/.msi & WinGet YAML from Unix
    ├── Build-Windows.ps1                    # Native Windows .zip/.msi & WinGet manifest builder
    └── Build-Packages.ps1                   # PowerShell cross-publish & staging helper
```

---

## Building Packages

Output artifacts are written to `packaging/dist/`. Requires the `.NET 10.0 SDK` (`dotnet`). All shell scripts are portable POSIX `/bin/sh`.
**Dependencies:** `.NET 10.0 SDK`, POSIX `/bin/sh`, and platform-specific tools as noted below.

> Note: Some scripts may require additional platform-specific tools to be installed. On FreeBSD, Dotnet  `10.0 SDK` must be installed via `/usr/ports/lang/dotnet` and Dotnet runtime must also be installed `/usr/ports/lang/dotnet-host`. It is not installed by default and may need to be built from ports or installed via the package manager.
  
### 1. Build for Current Host OS (Unix)
```sh
sh ./packaging/scripts/build-all.sh host
```

### 2. Linux Debian / Ubuntu (`.deb`)
Requires `dpkg-deb` (or `nfpm`):
```sh
sh ./packaging/scripts/build-deb.sh amd64
sh ./packaging/scripts/build-deb.sh arm64
```

### 3. Linux RHEL / Fedora / SUSE / openSUSE (`.rpm`)
Requires `rpmbuild` (`rpm-build` package) or `nfpm`:
```sh
sh ./packaging/scripts/build-rpm.sh amd64
sh ./packaging/scripts/build-rpm.sh arm64
```

### 4. Arch Linux (`.pkg.tar.zst`)
Requires `makepkg` (`pacman`, run as a non-root user) or `nfpm`:
```sh
sh ./packaging/scripts/build-arch.sh amd64
sh ./packaging/scripts/build-arch.sh arm64
```

### 5. macOS Installer (`.pkg`)
Run on macOS with Xcode Command Line Tools (`pkgbuild` and `productbuild`). Supports `arm64` (`osx-arm64`) and `amd64` (`osx-x64`):
```sh
sh ./packaging/scripts/build-macos-pkg.sh arm64
sh ./packaging/scripts/build-macos-pkg.sh amd64

# Optional Developer ID signing:
MACOS_SIGN_IDENTITY="Developer ID Installer: Your Name (TEAMID)" \
  sh ./packaging/scripts/build-macos-pkg.sh arm64
```

### 6. FreeBSD Package (`.pkg`)
Run on FreeBSD (`pkg create`):
```sh
sh ./packaging/scripts/build-freebsd-pkg.sh amd64
FREEBSD_MAJOR=14 sh ./packaging/scripts/build-freebsd-pkg.sh arm64
```

### 6b. OpenBSD Package (`.tgz`)
Run on OpenBSD (`/usr/sbin/pkg_create`; outputs `packaging/dist/openbsd-<arch>/flow-ui-<version>.tgz` to satisfy OpenBSD `packages-specs(7)`), or cross-publish a root bundle from another host:
```sh
sh ./packaging/scripts/build-openbsd-pkg.sh amd64
sh ./packaging/scripts/build-openbsd-pkg.sh arm64

# Optional signify(1) signing on OpenBSD:
OPENBSD_SIGNIFY_KEY="/etc/signify/my-pkg.sec" \
  sh ./packaging/scripts/build-openbsd-pkg.sh amd64
```

### 7. Windows (`.zip` & `.msi`) + WinGet (`ETLMadness.FlowUI`)
On Windows PowerShell (builds `.zip` with zero extra tools, builds `.msi` into `C:\Program Files\ETL Madness\Flow UI\` when WiX Toolset is installed via `dotnet tool install --global wix`, generates WinGet 1.6.0 manifests in `packaging/dist/winget/`, and runs `winget validate`):
```powershell
.\packaging\scripts\Build-Windows.ps1 -Format all -Arch amd64
# Or build both x64 and arm64:
.\packaging\scripts\Build-Windows.ps1 -Format all -Arch both
```

Cross-publish staging or `nfpm` builds from Windows PowerShell:
```powershell
.\packaging\scripts\Build-Packages.ps1 -Target stage-all -Arch amd64
.\packaging\scripts\Build-Packages.ps1 -Target deb -Arch amd64
```

From Linux/macOS/FreeBSD:
```sh
sh ./packaging/scripts/build-windows.sh amd64
```

See [winget/README.md](winget/README.md) for local `winget install --manifest` testing and Authenticode code signing.

---

## Installing, Managing OS Services & Uninstalling

### 1. Linux — `systemd` (Debian, Ubuntu, RHEL, Fedora, Rocky, Alma, SLES, openSUSE, Arch)

```sh
# Install package
sudo apt install ./packaging/dist/flow-ui_1.3.1-1_amd64.deb                # Debian / Ubuntu
sudo dnf install ./packaging/dist/flow-ui-1.3.1-1.x86_64.rpm               # RHEL / Fedora / Rocky / Alma
sudo zypper --no-gpg-checks install ./packaging/dist/flow-ui-1.3.1-1.x86_64.rpm  # SLES / openSUSE
sudo pacman -U ./packaging/dist/flow-ui-1.3.1-1-x86_64.pkg.tar.zst         # Arch Linux

# Activate PATH & INFOPATH in current shell
. /etc/profile.d/flow-ui.sh

# Configure go-flowAPI URL, bind address, and auth mode
sudo vi /etc/flow-ui/flow-ui.env

# Enable at boot and start
sudo systemctl enable --now flow-ui.service
sudo systemctl status flow-ui.service
sudo journalctl -u flow-ui.service -f

# Uninstall
sudo apt purge flow-ui          # Debian / Ubuntu
sudo dnf remove flow-ui         # RHEL / Fedora
sudo zypper remove flow-ui      # SLES / openSUSE
sudo pacman -Rns flow-ui        # Arch Linux
```

### 2. Linux — OpenRC (`Alpine`, `Gentoo`, `Artix`)

```sh
sudo install -m 0755 /opt/flow-ui/share/services/openrc/flow-ui /etc/init.d/flow-ui
sudo rc-update add flow-ui default
sudo rc-service flow-ui start
```

### 3. Linux — SysVinit (`/etc/init.d`)

```sh
sudo install -m 0755 /opt/flow-ui/share/services/sysvinit/flow-ui /etc/init.d/flow-ui
sudo update-rc.d flow-ui defaults   # or: sudo chkconfig --add flow-ui
sudo service flow-ui start
```

### 4. FreeBSD — `rc.d` (`/usr/local/etc/rc.d/flow_ui`)

```sh
# Install .NET runtime prerequisite and FlowUI package
sudo pkg install dotnet
sudo pkg add ./packaging/dist/flow-ui-1.3.1-freebsd-amd64.pkg

. /usr/local/etc/profile.d/flow-ui.sh
sudo vi /usr/local/etc/flow-ui/flow-ui.env
sudo sysrc flow_ui_enable="YES"
sudo service flow_ui start

# Uninstall
sudo pkg delete flow-ui
```

### 4b. OpenBSD — `rc.d` (`/etc/rc.d/flow_ui` & `rcctl`)

> **OpenBSD `.NET` Runtime Note:** OpenBSD does not currently ship an official `dotnet` package in the standard ports tree. Ensure a compatible `.NET 10` ASP.NET Core runtime (`dotnet`) is available in `PATH` or `/usr/local/bin/dotnet` before starting `flow_ui` on OpenBSD, or host `FlowUI` on Linux/FreeBSD/macOS/Windows pointed at your OpenBSD `go-flowAPI` server via `FLOW_API_URL`.

```sh
# Install native pkg_create package (-D unsigned for unsigned local archives; omit if signed)
doas pkg_add -D unsigned ./packaging/dist/openbsd-amd64/flow-ui-1.3.1.tgz

# (Or if installing a cross-published bundle built off-host:)
doas tar -xzphf ./packaging/dist/flow-ui-1.3.1-openbsd-amd64-bundle.tar.gz -C /
doas sh /opt/flow-ui/share/services/openbsd/+INSTALL flow-ui-1.3.1 POST-INSTALL

. /etc/profile.d/flow-ui.sh
doas vi /etc/flow-ui/flow-ui.env
doas rcctl enable flow_ui
doas rcctl start flow_ui
doas rcctl check flow_ui

# Uninstall
doas pkg_delete flow-ui
```

### 5. macOS — `launchd` (`LaunchDaemon`)

```sh
sudo installer -pkg ./packaging/dist/flow-ui-1.3.1-darwin-arm64.pkg -target /
eval "$(/usr/libexec/path_helper -s)"

sudo vi /opt/flow-ui/etc/flow-ui.env
sudo chown root:wheel /opt/flow-ui/etc/flow-ui.env
sudo chmod 0600 /opt/flow-ui/etc/flow-ui.env

sudo cp /opt/flow-ui/share/services/launchd/com.etlmadness.flow-ui.plist /Library/LaunchDaemons/
sudo chown root:wheel /Library/LaunchDaemons/com.etlmadness.flow-ui.plist
sudo chmod 0644 /Library/LaunchDaemons/com.etlmadness.flow-ui.plist
sudo launchctl bootstrap system /Library/LaunchDaemons/com.etlmadness.flow-ui.plist

# Uninstall
sudo launchctl bootout system/com.etlmadness.flow-ui 2>/dev/null || true
sudo rm -f /Library/LaunchDaemons/com.etlmadness.flow-ui.plist
sudo rm -rf /opt/flow-ui /private/etc/paths.d/flow-ui /private/etc/manpaths.d/flow-ui
sudo pkgutil --forget com.etlmadness.flow-ui
```

### 6. Windows — WinGet & Native Windows Service (`FlowUI` / SCM)

`FlowUI.exe` natively implements the Windows Service Control Manager dispatcher via `Microsoft.Extensions.Hosting.WindowsServices` — **no NSSM or third-party wrapper is required**.

```powershell
# Option A: Install via WinGet (or --manifest .\packaging\dist\winget\e\ETLMadness\FlowUI\1.3.1\)
winget install --id ETLMadness.FlowUI
winget install --id ETLMadness.FlowUI --scope machine --installer-type wix

# Option B: Silent MSI install into C:\Program Files\ETL Madness\Flow UI\
msiexec /i .\packaging\dist\flow-ui-1.3.1-windows-amd64.msi /qn /norestart

# Register and start the FlowUI Windows Service (elevated PowerShell)
& "C:\Program Files\ETL Madness\Flow UI\share\services\windows\Install-FlowUIService.ps1" `
    -ApiUrl "http://localhost:8080" `
    -ListenUrls "http://0.0.0.0:5185" `
    -Start

# Check status / restart / uninstall service & MSI
Get-Service -Name FlowUI
Restart-Service -Name FlowUI
& "C:\Program Files\ETL Madness\Flow UI\share\services\windows\Uninstall-FlowUIService.ps1"
msiexec /x .\packaging\dist\flow-ui-1.3.1-windows-amd64.msi /qn /norestart
```

---

## Viewing Documentation After Installation

```sh
# Unix Manual pages
man flow-ui
man 5 flow-ui.env

# Unix GNU Info pages
info flow-ui
```
