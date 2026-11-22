%global debug_package %{nil}
%define __strip /bin/true
%define _build_id_links none

Name:           flow-ui
Version:        %{_flow_version}
Release:        %{_flow_release}%{?dist}
Summary:        ETL Madness FlowUI — Flow Builder Studio (.NET 10 Blazor Server)
License:        MIT
URL:            https://github.com/etl-madness/flowUI
Source0:        flow-ui-%{_flow_version}-staged.tar.gz

%description
ETL Madness FlowUI (Flow Builder Studio) is an interactive .NET 10 ASP.NET Core
Blazor Server web application for visually designing, configuring, signing,
encrypting, validating, executing, and monitoring XML ETL pipelines powered by
go-flow and go-flowAPI. Installed isolated under /opt/flow-ui alongside
systemd, OpenRC, and SysVinit service templates and /etc/flow-ui/flow-ui.env.

%prep
%setup -q -c -n flow-ui-%{_flow_version}

%build
# Pre-published self-contained .NET 10 application and compressed docs are staged in Source0.

%install
rm -rf %{buildroot}
install -d -m 0755 %{buildroot}/opt/flow-ui/app
cp -a app/. %{buildroot}/opt/flow-ui/app/
chmod 0755 %{buildroot}/opt/flow-ui/app/FlowUI

install -d -m 0755 %{buildroot}/opt/flow-ui/bin
install -m 0755 bin/flow-ui %{buildroot}/opt/flow-ui/bin/flow-ui

install -d -m 0755 %{buildroot}/opt/flow-ui/share/man/man1
install -m 0644 doc/man1/flow-ui.1.gz %{buildroot}/opt/flow-ui/share/man/man1/flow-ui.1.gz

install -d -m 0755 %{buildroot}/opt/flow-ui/share/man/man5
install -m 0644 doc/man5/flow-ui.env.5.gz %{buildroot}/opt/flow-ui/share/man/man5/flow-ui.env.5.gz

install -d -m 0755 %{buildroot}/opt/flow-ui/share/info
install -m 0644 doc/info/flow-ui.info.gz %{buildroot}/opt/flow-ui/share/info/flow-ui.info.gz
install -m 0644 doc/info/dir %{buildroot}/opt/flow-ui/share/info/dir

install -d -m 0755 %{buildroot}/opt/flow-ui/share/doc
install -m 0644 guides/README.md %{buildroot}/opt/flow-ui/share/doc/README.md
install -m 0644 guides/DOCKER.md %{buildroot}/opt/flow-ui/share/doc/DOCKER.md

install -d -m 0755 %{buildroot}/opt/flow-ui/share/services/systemd
install -m 0644 services/systemd/flow-ui.service %{buildroot}/opt/flow-ui/share/services/systemd/flow-ui.service
install -d -m 0755 %{buildroot}/opt/flow-ui/share/services/openrc
install -m 0755 services/openrc/flow-ui %{buildroot}/opt/flow-ui/share/services/openrc/flow-ui
install -d -m 0755 %{buildroot}/opt/flow-ui/share/services/sysvinit
install -m 0755 services/sysvinit/flow-ui %{buildroot}/opt/flow-ui/share/services/sysvinit/flow-ui

install -d -m 0755 %{buildroot}/opt/flow-ui/share/licenses
install -m 0644 LICENSE.txt %{buildroot}/opt/flow-ui/share/licenses/LICENSE

install -d -m 0755 %{buildroot}/etc/profile.d
install -m 0644 profile.d/flow-ui.sh %{buildroot}/etc/profile.d/flow-ui.sh

install -d -m 0750 %{buildroot}/etc/flow-ui
install -m 0640 etc-env/flow-ui.env %{buildroot}/etc/flow-ui/flow-ui.env

install -d -m 0755 %{buildroot}/usr/lib/systemd/system
install -m 0644 services/systemd/flow-ui.service %{buildroot}/usr/lib/systemd/system/flow-ui.service

install -d -m 0750 %{buildroot}/var/lib/flow-ui
install -d -m 0750 %{buildroot}/var/log/flow-ui

%pre
getent group flowui >/dev/null 2>&1 || groupadd -r flowui || :
getent passwd flowui >/dev/null 2>&1 || useradd -r -g flowui -d /var/lib/flow-ui -s /sbin/nologin -c "ETL Madness FlowUI daemon" flowui || :

%post
if getent passwd flowui >/dev/null 2>&1; then
    chown flowui:flowui /var/lib/flow-ui /var/log/flow-ui 2>/dev/null || :
    chown root:flowui /etc/flow-ui /etc/flow-ui/flow-ui.env 2>/dev/null || :
fi
if [ -d /run/systemd/system ] && command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload >/dev/null 2>&1 || :
    if [ "$1" -ge 2 ] && systemctl is-active --quiet flow-ui.service >/dev/null 2>&1; then
        systemctl try-restart flow-ui.service >/dev/null 2>&1 || :
    fi
fi

%preun
if [ "$1" -eq 0 ]; then
    if [ -d /run/systemd/system ] && command -v systemctl >/dev/null 2>&1; then
        systemctl --no-reload disable --now flow-ui.service >/dev/null 2>&1 || :
    fi
fi

%postun
if [ -d /run/systemd/system ] && command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload >/dev/null 2>&1 || :
fi

%files
%license /opt/flow-ui/share/licenses/LICENSE
%dir /opt/flow-ui
/opt/flow-ui/app
%dir /opt/flow-ui/bin
/opt/flow-ui/bin/flow-ui
%dir /opt/flow-ui/share
%dir /opt/flow-ui/share/man
%dir /opt/flow-ui/share/man/man1
%dir /opt/flow-ui/share/man/man5
/opt/flow-ui/share/man/man1/flow-ui.1*
/opt/flow-ui/share/man/man5/flow-ui.env.5*
%dir /opt/flow-ui/share/info
/opt/flow-ui/share/info/flow-ui.info*
/opt/flow-ui/share/info/dir
%dir /opt/flow-ui/share/doc
/opt/flow-ui/share/doc/*.md
%dir /opt/flow-ui/share/services
%dir /opt/flow-ui/share/services/systemd
/opt/flow-ui/share/services/systemd/flow-ui.service
%dir /opt/flow-ui/share/services/openrc
/opt/flow-ui/share/services/openrc/flow-ui
%dir /opt/flow-ui/share/services/sysvinit
/opt/flow-ui/share/services/sysvinit/flow-ui
%dir /opt/flow-ui/share/licenses
/usr/lib/systemd/system/flow-ui.service
%dir /etc/flow-ui
%config(noreplace) /etc/flow-ui/flow-ui.env
%config(noreplace) /etc/profile.d/flow-ui.sh
%dir /var/lib/flow-ui
%dir /var/log/flow-ui
