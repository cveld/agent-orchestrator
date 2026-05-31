window.scrollToBottom = function (id) {
    const el = document.getElementById(id);
    if (el) el.scrollTop = el.scrollHeight;
};

window.focusElement = function (id) {
    const el = document.getElementById(id);
    if (el) el.focus();
};
