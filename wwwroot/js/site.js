// Global form helpers — single SweetAlert confirm + anti double-submit.
(function (global) {
    'use strict';

    var CONFIRMED_ATTR = 'data-hb-confirmed';
    var SUBMITTING_ATTR = 'data-hb-submitting';
    var BOUND_ATTR = 'data-hb-confirm-bound';

    var classPresets = {
        'confirm-deactivate': {
            title: 'Deactivate this record?',
            text: 'This record will no longer be available for selection.',
            icon: 'warning',
            confirmButtonText: 'Yes, deactivate',
            confirmButtonColor: '#dc2626'
        },
        'confirm-deactivate-user': {
            title: 'Deactivate this user?',
            text: 'This user will no longer be able to use the system.',
            icon: 'warning',
            confirmButtonText: 'Yes, deactivate',
            confirmButtonColor: '#dc2626'
        },
        'confirm-activate-user': {
            title: 'Activate this user?',
            text: 'This will restore login access for this user.',
            icon: 'question',
            confirmButtonText: 'Yes, activate',
            confirmButtonColor: '#16a34a'
        }
    };

    function isConfirmed(form) {
        return form.getAttribute(CONFIRMED_ATTR) === 'true';
    }

    function markConfirmed(form) {
        form.setAttribute(CONFIRMED_ATTR, 'true');
    }

    function resetFormState(form) {
        form.removeAttribute(CONFIRMED_ATTR);
        form.removeAttribute(SUBMITTING_ATTR);
        form.removeAttribute('data-submitted');

        form.querySelectorAll('button[type="submit"], input[type="submit"], button:not([type])').forEach(function (button) {
            if (button.type && button.type !== 'submit') {
                return;
            }

            button.disabled = false;

            if (button.dataset.originalText) {
                if (button.tagName.toLowerCase() === 'button') {
                    button.innerHTML = button.dataset.originalText;
                } else {
                    button.value = button.dataset.originalText;
                }
            }
        });
    }

    function getConfirmOptions(form) {
        var options = {
            title: form.dataset.hbConfirmTitle || 'Save changes?',
            text: form.dataset.hbConfirmText || 'Please confirm before saving this record.',
            icon: form.dataset.hbConfirmIcon || 'question',
            confirmButtonText: form.dataset.hbConfirmButton || 'Yes, save it',
            cancelButtonText: form.dataset.hbCancelButton || 'No, cancel',
            confirmButtonColor: form.dataset.hbConfirmColor || '#2563eb',
            cancelButtonColor: '#64748b'
        };

        Object.keys(classPresets).forEach(function (cls) {
            if (form.classList.contains(cls)) {
                Object.assign(options, classPresets[cls]);
            }
        });

        if (form.dataset.hbConfirmTitle) {
            options.title = form.dataset.hbConfirmTitle;
        }
        if (form.dataset.hbConfirmText) {
            options.text = form.dataset.hbConfirmText;
        }
        if (form.dataset.hbConfirmButton) {
            options.confirmButtonText = form.dataset.hbConfirmButton;
        }
        if (form.dataset.hbConfirmIcon) {
            options.icon = form.dataset.hbConfirmIcon;
        }
        if (form.dataset.hbConfirmColor) {
            options.confirmButtonColor = form.dataset.hbConfirmColor;
        }
        if (form.dataset.hbConfirmHtml) {
            options.html = form.dataset.hbConfirmHtml;
            delete options.text;
        }

        return options;
    }

    function buildSwalOptions(form, overrides) {
        var options = getConfirmOptions(form);
        if (overrides) {
            Object.assign(options, overrides);
            if (overrides.html) {
                delete options.text;
            }
        }
        return options;
    }

    function lockSubmitButtons(form) {
        form.querySelectorAll('button[type="submit"], input[type="submit"], button:not([type])').forEach(function (button) {
            if (button.type && button.type !== 'submit') {
                return;
            }

            button.dataset.originalText = button.innerHTML || button.value || '';

            if (button.tagName.toLowerCase() === 'button') {
                button.disabled = true;
                button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Processing...';
            } else {
                button.disabled = true;
                button.value = 'Processing...';
            }
        });
    }

    function confirmAndSubmit(form, options) {
        if (isConfirmed(form)) {
            lockSubmitButtons(form);
            // form.submit() is used here (not requestSubmit) for reliable navigation.
            // requestSubmit() called from a Promise microtask context drops the browser
            // navigation in some browsers; form.submit() bypasses that race condition.
            form.submit();
            return;
        }

        if (form.getAttribute(SUBMITTING_ATTR) === 'true') {
            return;
        }

        form.setAttribute(SUBMITTING_ATTR, 'true');

        var swalOptions = Object.assign({
            showCancelButton: true
        }, buildSwalOptions(form, options));

        Swal.fire(swalOptions).then(function (result) {
            form.removeAttribute(SUBMITTING_ATTR);

            if (!result.isConfirmed) {
                return;
            }

            markConfirmed(form);
            lockSubmitButtons(form);
            // Use form.submit() for unconditionally reliable cross-browser navigation.
            // requestSubmit() fires submit event handlers again (unnecessary here, since
            // validation already passed) and its async-context navigation can be silently
            // dropped in some browser/OS combinations.
            form.submit();
        });
    }

    function bindConfirmForm(form) {
        if (form.getAttribute(BOUND_ATTR) === 'true') {
            return;
        }

        if (form.dataset.hbCustomConfirm === 'true') {
            return;
        }

        form.setAttribute(BOUND_ATTR, 'true');

        form.addEventListener('submit', function (e) {
            if (isConfirmed(form)) {
                return;
            }

            // When jQuery unobtrusive validation is active on this form, only show
            // the confirmation dialog if the form is already valid.  If invalid,
            // restore any stale loading state and return early so jQuery can display
            // its field-level error messages without the dialog appearing on top.
            if (window.jQuery) {
                var $form = window.jQuery(form);
                var validator = $form.data('validator');
                if (validator && !$form.valid()) {
                    // Explicitly restore buttons to their original state so they never
                    // show "Processing…" when validation has failed.
                    resetFormState(form);
                    return;
                }
            }

            e.preventDefault();
            e.stopPropagation();

            confirmAndSubmit(form, getConfirmOptions(form));
        });
    }

    function initConfirmForms() {
        var selector = [
            'form.confirm-submit',
            'form.confirm-deactivate',
            'form.confirm-deactivate-user',
            'form.confirm-activate-user',
            'form[data-hb-confirm]'
        ].join(',');

        document.querySelectorAll(selector).forEach(bindConfirmForm);

        document.querySelectorAll('.modal').forEach(function (modal) {
            modal.addEventListener('show.bs.modal', function () {
                modal.querySelectorAll('form').forEach(resetFormState);
            });
        });

        document.querySelectorAll('[data-hb-confirm-submit]').forEach(function (button) {
            button.addEventListener('click', function () {
                var formId = button.getAttribute('data-hb-confirm-submit');
                var form = formId ? document.getElementById(formId) : null;

                if (!form) {
                    return;
                }

                if (button.dataset.hbValidateFn && global[button.dataset.hbValidateFn]) {
                    if (!global[button.dataset.hbValidateFn](form)) {
                        return;
                    }
                }

                var options = getConfirmOptions(form);

                if (button.dataset.hbConfirmTitle) {
                    options.title = button.dataset.hbConfirmTitle;
                }
                if (button.dataset.hbConfirmText) {
                    options.text = button.dataset.hbConfirmText;
                }
                if (button.dataset.hbConfirmButton) {
                    options.confirmButtonText = button.dataset.hbConfirmButton;
                }

                confirmAndSubmit(form, options);
            });
        });
    }

    function initAntiDoubleSubmit() {
        document.querySelectorAll('form').forEach(function (form) {
            form.addEventListener('submit', function (e) {
                var submitter = e.submitter;

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

                    // Always mark as submitted to guard against rapid re-submits.
                    form.dataset.submitted = 'true';

                    // HbForm-managed confirm forms (confirm-submit, confirm-deactivate,
                    // etc.) explicitly call lockSubmitButtons inside confirmAndSubmit,
                    // AFTER the user clicks "Yes" in the SweetAlert dialog.  Locking
                    // here would show "Processing…" on forms that failed client-side
                    // validation — the exact bug this guard is designed to prevent.
                    if (form.getAttribute(BOUND_ATTR) === 'true') {
                        return;
                    }

                    lockSubmitButtons(form);
                });
            });
        });
    }

    global.HbForm = {
        isConfirmed: isConfirmed,
        markConfirmed: markConfirmed,
        resetFormState: resetFormState,
        getConfirmOptions: getConfirmOptions,
        confirmAndSubmit: confirmAndSubmit
    };

    document.addEventListener('DOMContentLoaded', function () {
        initConfirmForms();
        initAntiDoubleSubmit();
    });
})(window);
