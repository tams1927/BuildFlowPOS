// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
// ============================================
// GLOBAL ANTI DOUBLE-SUBMIT PROTECTION
// Prevents users from submitting the same form multiple times.
// Applies automatically to normal POST forms.
// ============================================

document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('form').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            const submitter = e.submitter;

            if (submitter && submitter.dataset.noDisable === 'true') {
                return;
            }

            if ((form.getAttribute('method') || '').toLowerCase() === 'get') {
                return;
            }

            if (form.dataset.submitted === 'true') {
                e.preventDefault();
                e.stopPropagation();
                return;
            }

            queueMicrotask(function () {
                if (e.defaultPrevented) {
                    return;
                }

                form.dataset.submitted = 'true';

                const submitButtons = form.querySelectorAll('button[type="submit"], input[type="submit"], button:not([type])');

                submitButtons.forEach(function (button) {
                    button.dataset.originalText = button.innerHTML || button.value || '';

                    if (button.tagName.toLowerCase() === 'button') {
                        button.disabled = true;
                        button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Processing...';
                    } else {
                        button.disabled = true;
                        button.value = 'Processing...';
                    }
                });
            });
        });
    });
});