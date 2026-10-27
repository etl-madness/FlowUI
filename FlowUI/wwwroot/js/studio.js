window.flowStudio = {
    getSavedTheme: function () {
        try {
            const saved = localStorage.getItem("flow_studio_theme");
            if (saved) {
                document.documentElement.setAttribute("data-theme", saved);
            }
            return saved || "";
        } catch {
            return "";
        }
    },
    saveTheme: function (themeId) {
        try {
            if (themeId) {
                localStorage.setItem("flow_studio_theme", themeId);
                document.documentElement.setAttribute("data-theme", themeId);
                this.renderMermaid(".doc-guide-body", true);
            }
        } catch {
            // Ignore storage errors in restricted browsers
        }
    },
    renderMermaid: async function (containerSelector, forceRerender) {
        if (typeof window.mermaid === "undefined") {
            return;
        }

        const root = containerSelector
            ? document.querySelector(containerSelector)
            : document;
        if (!root) {
            return;
        }

        const blocks = root.querySelectorAll(".mermaid");
        if (!blocks || blocks.length === 0) {
            return;
        }

        const lightThemes = new Set([
            "daylight",
            "github-light",
            "solarized-light",
            "nord-light",
            "latte",
            "paper"
        ]);
        const activeThemeAttr = (document.documentElement.getAttribute("data-theme") || "midnight").toLowerCase();
        const mermaidTheme = lightThemes.has(activeThemeAttr) ? "default" : "dark";

        window.mermaid.initialize({
            startOnLoad: false,
            theme: mermaidTheme,
            securityLevel: "loose",
            flowchart: {
                htmlLabels: true,
                curve: "basis"
            }
        });

        const allowedHtmlTags = new Set([
            "br", "b", "i", "u", "s", "strong", "em", "code", "pre", "span", "small", "sub", "sup", "p", "div"
        ]);

        const sanitizeMermaidXmlTags = function (raw) {
            const decoded = raw
                .replace(/&lt;/g, "<")
                .replace(/&gt;/g, ">")
                .replace(/&amp;/g, "&")
                .replace(/&quot;/g, "\"")
                .replace(/&#39;/g, "'");

            return decoded.replace(/<(\/?([a-zA-Z_][\w-]*)(?:\s[^<>]*)?\/?)>/g, function (match, inner, tagName) {
                if (allowedHtmlTags.has(tagName.toLowerCase())) {
                    return match;
                }
                return "&lt;" + inner + "&gt;";
            });
        };

        let index = 0;
        for (const el of blocks) {
            const renderedTheme = el.getAttribute("data-mermaid-rendered");
            if (!forceRerender && renderedTheme === mermaidTheme && el.querySelector("svg")) {
                continue;
            }

            let rawSource = el.getAttribute("data-mermaid");
            if (!rawSource) {
                rawSource = (el.textContent || "").trim();
                el.setAttribute("data-mermaid", rawSource);
            }

            const graphDefinition = sanitizeMermaidXmlTags(rawSource).trim();
            if (!graphDefinition) {
                continue;
            }

            try {
                const renderId = "mermaid-doc-" + Date.now() + "-" + (++index) + "-" + Math.random().toString(36).slice(2, 7);
                const result = await window.mermaid.render(renderId, graphDefinition);
                el.innerHTML = result.svg;
                el.setAttribute("data-mermaid-rendered", mermaidTheme);
            } catch (err) {
                console.error("Failed to render Mermaid diagram:", err);
            }
        }
    },
    copyToClipboard: async function (text) {
        if (!navigator.clipboard) {
            const ta = document.createElement("textarea");
            ta.value = text;
            document.body.appendChild(ta);
            ta.select();
            document.execCommand("copy");
            document.body.removeChild(ta);
            return true;
        }
        await navigator.clipboard.writeText(text);
        return true;
    },
    downloadXmlFile: function (filename, content) {
        const blob = new Blob([content], { type: "application/xml;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = filename || "pipeline.xml";
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }
};

// Apply saved theme immediately on page load
window.flowStudio.getSavedTheme();
