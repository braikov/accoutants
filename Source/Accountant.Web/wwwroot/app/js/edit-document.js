(function () {
  'use strict';

  const tableBody = document.querySelector('[data-role="lines-body"]');
  const template = document.getElementById('line-template');
  if (!tableBody || !template) return;

  document.querySelector('[data-action="add-line"]').addEventListener('click', () => {
    const nextIndex = tableBody.querySelectorAll('[data-role="line-row"]').length;
    const html = template.innerHTML.replaceAll('__INDEX__', String(nextIndex));
    const wrapper = document.createElement('tbody');
    wrapper.innerHTML = html.trim();
    const newRow = wrapper.querySelector('[data-role="line-row"]');
    tableBody.appendChild(newRow);
    if (window.jQuery && window.jQuery.validator) {
      window.jQuery('form').removeData('validator').removeData('unobtrusiveValidation');
      window.jQuery.validator.unobtrusive.parse('form');
    }
  });

  tableBody.addEventListener('click', e => {
    const remove = e.target.closest('[data-action="remove-line"]');
    if (!remove) return;
    remove.closest('[data-role="line-row"]').remove();
    reindex();
  });

  function reindex() {
    const rows = tableBody.querySelectorAll('[data-role="line-row"]');
    rows.forEach((row, i) => {
      row.querySelectorAll('input, select').forEach(el => {
        if (el.name) el.name = el.name.replace(/Lines\[\d+\]/, `Lines[${i}]`);
        if (el.id) el.id = el.id.replace(/Lines_\d+__/, `Lines_${i}__`);
      });
    });
  }
})();
