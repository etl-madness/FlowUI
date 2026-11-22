# ETL Madness FlowUI environment setup (/opt/flow-ui).
# Appends /opt/flow-ui/bin to PATH so existing system binaries are never shadowed.
# Safe under POSIX sh, ksh, bash, dash, and zsh, including 'set -u' (nounset).

case ":${PATH:-}:" in
    *:/opt/flow-ui/bin:*) ;;
    *)
        if [ -n "${PATH:-}" ]; then
            export PATH="${PATH}:/opt/flow-ui/bin"
        else
            export PATH="/opt/flow-ui/bin"
        fi
        ;;
esac

# Expose /opt/flow-ui/share/info to GNU info while retaining default system paths
# via the trailing colon.
case ":${INFOPATH:-}:" in
    *:/opt/flow-ui/share/info:*) ;;
    *)
        _flowui_infopath="${INFOPATH:-}"
        _flowui_infopath="${_flowui_infopath%:}"
        if [ -n "$_flowui_infopath" ]; then
            export INFOPATH="${_flowui_infopath}:/opt/flow-ui/share/info:"
        else
            export INFOPATH="/opt/flow-ui/share/info:"
        fi
        unset _flowui_infopath
        ;;
esac

# On OpenBSD, man(1) does not auto-derive ../share/man from PATH; a leading
# colon in MANPATH appends to the default /etc/man.conf search path.
if [ "$(uname -s 2>/dev/null || true)" = "OpenBSD" ]; then
    case ":${MANPATH:-}:" in
        *:/opt/flow-ui/share/man:*) ;;
        *)
            if [ -n "${MANPATH:-}" ]; then
                export MANPATH="${MANPATH}:/opt/flow-ui/share/man"
            else
                export MANPATH=":/opt/flow-ui/share/man"
            fi
            ;;
    esac
fi
