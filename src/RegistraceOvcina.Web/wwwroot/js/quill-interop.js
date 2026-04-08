window.quillInterop = {
    _instances: {},

    create: function (editorId, toolbarId, initialHtml) {
        const quill = new Quill('#' + editorId, {
            theme: 'snow',
            modules: {
                toolbar: '#' + toolbarId
            }
        });

        if (initialHtml) {
            quill.root.innerHTML = initialHtml;
        }

        this._instances[editorId] = quill;
        return true;
    },

    getHtml: function (editorId) {
        const quill = this._instances[editorId];
        if (!quill) return '';
        return quill.root.innerHTML;
    },

    setHtml: function (editorId, html) {
        const quill = this._instances[editorId];
        if (!quill) return;
        quill.root.innerHTML = html || '';
    }
};
