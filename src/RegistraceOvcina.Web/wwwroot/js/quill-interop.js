window.quillInterop = {
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

        quill._dotnetEditorId = editorId;
        return true;
    },

    getHtml: function (editorId) {
        const container = document.getElementById(editorId);
        if (!container) return '';
        const quill = Quill.find(container);
        if (!quill) return '';
        return quill.root.innerHTML;
    },

    setHtml: function (editorId, html) {
        const container = document.getElementById(editorId);
        if (!container) return;
        const quill = Quill.find(container);
        if (!quill) return;
        quill.root.innerHTML = html || '';
    }
};
