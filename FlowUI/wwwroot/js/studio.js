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
                this.renderMermaid("#handbook-content-scroll", true);
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
    },
    _handbookScrollCleanup: null,
    ensureHandbookTreeNodeVisible: function (treeContainer, targetNode) {
        if (!treeContainer || !targetNode) {
            return;
        }
        const treeRect = treeContainer.getBoundingClientRect();
        const nodeRect = targetNode.getBoundingClientRect();
        if (treeRect.height <= 0) {
            return;
        }
        const padding = 12;
        if (nodeRect.top < treeRect.top + padding) {
            const delta = nodeRect.top - (treeRect.top + padding);
            treeContainer.scrollTop = Math.max(0, treeContainer.scrollTop + delta);
        } else if (nodeRect.bottom > treeRect.bottom - padding) {
            const delta = nodeRect.bottom - (treeRect.bottom - padding);
            treeContainer.scrollTop = Math.max(0, treeContainer.scrollTop + delta);
        }
    },
    syncHandbookActiveState: function (contentEl, treeEl, dotNetHelper) {
        if (!contentEl || !treeEl) {
            return;
        }
        const sections = contentEl.querySelectorAll(".handbook-section[data-handbook-key]");
        if (!sections || sections.length === 0) {
            return;
        }

        const contentRect = contentEl.getBoundingClientRect();
        const probeY = contentRect.top + Math.min(160, contentRect.height * 0.24);

        let activeSection = sections[0];
        const isAtBottom = Math.abs((contentEl.scrollHeight - contentEl.scrollTop) - contentEl.clientHeight) < 8;
        if (isAtBottom && sections.length > 0) {
            activeSection = sections[sections.length - 1];
        } else {
            for (const sec of sections) {
                const rect = sec.getBoundingClientRect();
                if (rect.top <= probeY) {
                    activeSection = sec;
                } else {
                    break;
                }
            }
        }

        const activeKey = activeSection.getAttribute("data-handbook-key") || "";
        const activeCategory = activeSection.getAttribute("data-handbook-category") || "";

        // Also check subheadings (h2/h3) inside the active section
        let activeHeadingId = "";
        const headings = activeSection.querySelectorAll("h2[id], h3[id]");
        for (const h of headings) {
            const hRect = h.getBoundingClientRect();
            if (hRect.top <= probeY + 24) {
                activeHeadingId = h.id;
            } else {
                break;
            }
        }

        // Update active classes on tree items
        const allTreeItems = treeEl.querySelectorAll("[data-tree-key]");
        let activeTreeItem = null;
        for (const item of allTreeItems) {
            const key = item.getAttribute("data-tree-key");
            const wrapper = item.closest(".handbook-tree-node");
            if (key === activeKey) {
                item.classList.add("active");
                if (wrapper) wrapper.classList.add("active");
                activeTreeItem = item;
            } else {
                item.classList.remove("active");
                if (wrapper) wrapper.classList.remove("active");
            }
        }

        const allSubheadings = treeEl.querySelectorAll("[data-tree-heading]");
        let activeSubheadingEl = null;
        for (const sub of allSubheadings) {
            const hid = sub.getAttribute("data-tree-heading");
            if (activeHeadingId && hid === activeHeadingId) {
                sub.classList.add("active");
                activeSubheadingEl = sub;
            } else {
                sub.classList.remove("active");
            }
        }

        const allCategories = treeEl.querySelectorAll("[data-tree-category]");
        for (const cat of allCategories) {
            const cName = cat.getAttribute("data-tree-category");
            if (cName === activeCategory) {
                cat.classList.add("active-category");
            } else {
                cat.classList.remove("active-category");
            }
        }

        const nodeToKeepVisible = (activeSubheadingEl && activeSubheadingEl.offsetParent !== null)
            ? activeSubheadingEl
            : activeTreeItem;

        if (nodeToKeepVisible) {
            this.ensureHandbookTreeNodeVisible(treeEl, nodeToKeepVisible);
        }

        if (dotNetHelper && this._lastReportedHandbookKey !== activeKey) {
            this._lastReportedHandbookKey = activeKey;
            try {
                dotNetHelper.invokeMethodAsync("OnScrollActiveKeyChanged", activeKey, activeHeadingId);
            } catch {
                // Ignore if circuit disposed
            }
        }
    },
    initHandbookScrollSpy: function (contentSelector, treeSelector, dotNetHelper) {
        if (this._handbookScrollCleanup) {
            this._handbookScrollCleanup();
            this._handbookScrollCleanup = null;
        }

        const contentEl = document.querySelector(contentSelector || "#handbook-content-scroll");
        const treeEl = document.querySelector(treeSelector || "#handbook-tree-scroll");
        if (!contentEl || !treeEl) {
            return;
        }

        let ticking = false;
        const self = this;
        const onScroll = function () {
            if (!ticking) {
                window.requestAnimationFrame(function () {
                    self.syncHandbookActiveState(contentEl, treeEl, dotNetHelper);
                    ticking = false;
                });
                ticking = true;
            }
        };

        contentEl.addEventListener("scroll", onScroll, { passive: true });
        this._handbookScrollCleanup = function () {
            contentEl.removeEventListener("scroll", onScroll);
        };

        // Check if URL has a hash target on initial load
        if (window.location.hash && window.location.hash.length > 1) {
            const hashId = decodeURIComponent(window.location.hash.substring(1));
            const targetEl = document.getElementById(hashId);
            if (targetEl && contentEl.contains(targetEl)) {
                const contentRect = contentEl.getBoundingClientRect();
                const targetRect = targetEl.getBoundingClientRect();
                contentEl.scrollTop += (targetRect.top - contentRect.top) - 18;
            }
        }

        this.syncHandbookActiveState(contentEl, treeEl, dotNetHelper);
    },
    scrollToHandbookElement: function (elementId, treeKey) {
        const contentEl = document.getElementById("handbook-content-scroll");
        const treeEl = document.getElementById("handbook-tree-scroll");
        const targetEl = document.getElementById(elementId);

        if (contentEl && targetEl) {
            const contentRect = contentEl.getBoundingClientRect();
            const targetRect = targetEl.getBoundingClientRect();
            const targetTop = contentEl.scrollTop + (targetRect.top - contentRect.top) - 16;
            contentEl.scrollTop = Math.max(0, targetTop);
            if (treeEl) {
                this.syncHandbookActiveState(contentEl, treeEl, null);
            }
        }
    }
};

// Apply saved theme immediately on page load
window.flowStudio.getSavedTheme();
