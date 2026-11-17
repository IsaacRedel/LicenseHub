// Theme switch for the admin panel. Loaded as a module by ThemeToggle; never inline (CSP script-src 'self').
const COOKIE = "lh-theme";
const THEMES = ["arquivo", "tatico"];
const ONE_YEAR_SECONDS = 365 * 24 * 60 * 60;

// The explicit theme on <html>, or the one the CSS picked from the system preference.
export function current() {
    const chosen = document.documentElement.getAttribute("data-theme");
    if (THEMES.includes(chosen)) {
        return chosen;
    }

    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "tatico" : "arquivo";
}

// Switches to the other theme and remembers it; the server reads the cookie on the next page load.
// Not HttpOnly on purpose: it holds no sensitive data.
export function toggle() {
    const next = current() === "tatico" ? "arquivo" : "tatico";
    document.documentElement.setAttribute("data-theme", next);
    document.cookie = `${COOKIE}=${next}; Path=/; Max-Age=${ONE_YEAR_SECONDS}; SameSite=Lax`;
    return next;
}
