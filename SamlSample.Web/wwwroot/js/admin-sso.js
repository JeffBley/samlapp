// Admin SSO: Bootstrap modal wire-up and auto-submit helpers.
// Loaded globally; all handlers are guarded with null checks.

document.addEventListener('DOMContentLoaded', function () {

    // Delete-app confirmation modal (Sso/Index)
    var deleteAppModal = document.getElementById('deleteAppModal');
    if (deleteAppModal) {
        deleteAppModal.addEventListener('show.bs.modal', function (event) {
            var button = event.relatedTarget;
            document.getElementById('deleteAppId').value = button.getAttribute('data-app-id');
            document.getElementById('deleteAppName').textContent = button.getAttribute('data-app-name');
        });
    }

    // Delete-cert confirmation modal (Sso/Edit)
    var deleteCertModal = document.getElementById('deleteCertModal');
    if (deleteCertModal) {
        deleteCertModal.addEventListener('show.bs.modal', function (event) {
            var button = event.relatedTarget;
            document.getElementById('deleteCertId').value = button.getAttribute('data-cert-id');
            document.getElementById('deleteCertThumbprint').textContent = button.getAttribute('data-cert-thumbprint');
        });
    }

    // Auto-submit the enclosing form when a [data-auto-submit] select changes (AppControls/Index)
    document.querySelectorAll('select[data-auto-submit]').forEach(function (select) {
        select.addEventListener('change', function () { this.form.submit(); });
    });

});
