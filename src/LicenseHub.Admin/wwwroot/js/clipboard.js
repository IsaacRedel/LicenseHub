// Copies text to the clipboard for the issue-license dialog. Returns false when the browser refuses
// (clipboard access needs a secure context such as https or localhost).
export async function copy(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}
