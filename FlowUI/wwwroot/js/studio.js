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
            }
        } catch {
            // Ignore storage errors in restricted browsers
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
