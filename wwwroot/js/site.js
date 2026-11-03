// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener('DOMContentLoaded', function () {
    // ----- Show / hide password -----
    document.querySelectorAll('[data-toggle-password]').forEach(function (button) {
        button.addEventListener('click', function () {
            var input = document.getElementById(button.dataset.togglePassword);
            if (!input) return;

            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            button.textContent = show ? 'Hide' : 'Show';
            button.setAttribute('aria-pressed', String(show));
        });
    });

    // ----- Confirm-password matching -----
    document.querySelectorAll('input[data-match]').forEach(function (confirmInput) {
        var original = document.getElementById(confirmInput.dataset.match);
        if (!original) return;

        function check() {
            confirmInput.setCustomValidity(
                confirmInput.value && confirmInput.value !== original.value ? 'Passwords do not match.' : ''
            );
        }

        original.addEventListener('input', check);
        confirmInput.addEventListener('input', check);
    });

    // ----- Bootstrap-style validation feedback on submit -----
    document.querySelectorAll('form[data-validate]').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            if (!form.checkValidity()) {
                event.preventDefault();
                event.stopPropagation();
            }
            form.classList.add('was-validated');
        });
    });
});

