// Submits the logout form of /sessao-expirada at once, so the expired session cookie is cleared with a POST
// carrying the antiforgery token. A file, not inline: the CSP allows only same-origin scripts.
document.getElementById("session-expired-form")?.submit();
