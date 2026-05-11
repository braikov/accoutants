(function () {
  'use strict';

  const root = document.querySelector('.workspace');
  if (!root) return;

  const folderId = root.dataset.folderId ? parseInt(root.dataset.folderId, 10) : null;
  const antiForgeryToken = document.querySelector('input[name="__RequestVerificationToken"]').value;
  const grid = root.querySelector('[data-role="documents-grid"]');
  const dropZone = root.querySelector('[data-role="drop-zone"]');
  const fileInput = root.querySelector('#file-input');
  const progressList = root.querySelector('[data-role="upload-progress"]');
  const processBtn = root.querySelector('[data-action="process"]');
  const newFolderDialog = document.getElementById('new-folder-dialog');

  const ACCEPTED = new Set([
    'application/pdf', 'image/jpeg', 'image/png', 'image/webp',
    'image/gif', 'image/tiff', 'image/bmp',
  ]);

  // ---- Upload (drag-drop + file input) ------------------------------------

  ['dragenter', 'dragover'].forEach(evt => {
    dropZone.addEventListener(evt, e => {
      e.preventDefault();
      e.stopPropagation();
      dropZone.classList.add('is-dragging');
    });
  });
  ['dragleave', 'drop'].forEach(evt => {
    dropZone.addEventListener(evt, e => {
      e.preventDefault();
      e.stopPropagation();
      dropZone.classList.remove('is-dragging');
    });
  });
  dropZone.addEventListener('drop', e => {
    handleFiles(e.dataTransfer.files);
  });
  fileInput.addEventListener('change', () => {
    handleFiles(fileInput.files);
    fileInput.value = '';
  });

  function handleFiles(fileList) {
    const files = Array.from(fileList).filter(f => ACCEPTED.has(f.type));
    if (files.length === 0) return;
    files.forEach(uploadOne);
  }

  function uploadOne(file) {
    const item = document.createElement('li');
    item.className = 'upload-item';
    item.innerHTML = `
      <span class="upload-name">${escapeHtml(file.name)}</span>
      <progress max="100" value="0"></progress>
      <span class="upload-status">…</span>`;
    progressList.appendChild(item);
    const bar = item.querySelector('progress');
    const status = item.querySelector('.upload-status');

    const formData = new FormData();
    formData.append('files', file);
    formData.append('__RequestVerificationToken', antiForgeryToken);

    const url = folderId
      ? `/App/Documents/Upload?folderId=${folderId}`
      : '/App/Documents/Upload';

    const xhr = new XMLHttpRequest();
    xhr.open('POST', url);
    xhr.setRequestHeader('RequestVerificationToken', antiForgeryToken);
    xhr.upload.addEventListener('progress', e => {
      if (e.lengthComputable) {
        bar.value = Math.round((e.loaded / e.total) * 100);
      }
    });
    xhr.addEventListener('load', () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          const json = JSON.parse(xhr.responseText);
          if (json.created && json.created.length > 0) {
            json.created.forEach(addDocumentCard);
            status.textContent = 'Готово';
            status.classList.add('ok');
            setTimeout(() => item.remove(), 1500);
          } else {
            status.textContent = 'Грешка';
            status.classList.add('err');
          }
        } catch (e) {
          status.textContent = 'Грешка';
          status.classList.add('err');
        }
      } else {
        status.textContent = `HTTP ${xhr.status}`;
        status.classList.add('err');
      }
    });
    xhr.addEventListener('error', () => {
      status.textContent = 'Мрежова грешка';
      status.classList.add('err');
    });
    xhr.send(formData);
  }

  function addDocumentCard(doc) {
    // Remove empty-state placeholder on first upload.
    const empty = grid.querySelector('.empty-state');
    if (empty) empty.remove();

    const card = document.createElement('article');
    card.className = 'document-card';
    card.dataset.docId = doc.id;
    card.dataset.status = doc.status;
    const thumbHtml = doc.hasThumbnail
      ? `<img src="/App/Documents/Thumbnail/${doc.id}" alt="" loading="lazy" />`
      : `<span class="document-thumb-placeholder">${doc.contentType === 'application/pdf' ? 'PDF' : 'DOC'}</span>`;
    card.innerHTML = `
      <label class="document-checkbox"><input type="checkbox" data-role="select" /></label>
      <a class="document-thumb" href="/App/Documents/Detail/${doc.id}" title="${escapeHtml(doc.originalFileName)}">${thumbHtml}</a>
      <div class="document-meta">
        <span class="document-name">${escapeHtml(doc.originalFileName)}</span>
        <span class="document-badge status-${doc.status.toLowerCase()}" data-role="status-badge">Качен</span>
      </div>`;
    grid.prepend(card);
    refreshSelectionState();
  }

  // ---- Selection + process ------------------------------------------------

  grid.addEventListener('change', e => {
    if (e.target.matches('[data-role="select"]')) refreshSelectionState();
  });

  function refreshSelectionState() {
    const selected = collectSelectedIds();
    processBtn.disabled = selected.length === 0;
    processBtn.textContent = selected.length === 0
      ? 'Обработи избраните'
      : `Обработи (${selected.length})`;
  }

  function collectSelectedIds() {
    return Array.from(grid.querySelectorAll('[data-role="select"]:checked'))
      .map(cb => parseInt(cb.closest('.document-card').dataset.docId, 10));
  }

  processBtn.addEventListener('click', async () => {
    const ids = collectSelectedIds();
    if (ids.length === 0) return;
    processBtn.disabled = true;

    const form = new FormData();
    ids.forEach(id => form.append('documentIds', id));
    form.append('__RequestVerificationToken', antiForgeryToken);

    const r = await fetch('/App/Documents/EnqueueExtraction', {
      method: 'POST',
      body: form,
      headers: { 'RequestVerificationToken': antiForgeryToken },
      credentials: 'same-origin',
    });
    if (!r.ok) {
      alert('Грешка при стартиране на обработката.');
      refreshSelectionState();
      return;
    }
    const json = await r.json();
    (json.queued || []).forEach(id => {
      const card = grid.querySelector(`[data-doc-id="${id}"]`);
      if (card) updateStatusBadge(card, 'Queued');
    });
    // Uncheck and refresh button label.
    grid.querySelectorAll('[data-role="select"]:checked').forEach(cb => cb.checked = false);
    refreshSelectionState();
  });

  // ---- Polling ------------------------------------------------------------

  const POLL_INTERVAL_MS = 2500;
  let pollTimer = null;

  function startPolling() {
    if (pollTimer) return;
    pollTimer = setInterval(poll, POLL_INTERVAL_MS);
  }
  function stopPolling() {
    if (pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  }

  async function poll() {
    if (document.visibilityState !== 'visible') return;
    const active = Array.from(grid.querySelectorAll('.document-card'))
      .filter(c => ['Queued', 'Processing'].includes(c.dataset.status))
      .map(c => parseInt(c.dataset.docId, 10));
    if (active.length === 0) return;
    try {
      const r = await fetch(`/App/Workspace/Statuses?ids=${active.join(',')}`);
      if (!r.ok) return;
      const rows = await r.json();
      rows.forEach(row => {
        const card = grid.querySelector(`[data-doc-id="${row.id}"]`);
        if (!card) return;
        if (card.dataset.status !== row.status) {
          updateStatusBadge(card, row.status);
        }
        if (row.hasThumbnail) {
          const img = card.querySelector('.document-thumb img');
          if (!img) {
            const ph = card.querySelector('.document-thumb-placeholder');
            if (ph) {
              const newImg = document.createElement('img');
              newImg.src = `/App/Documents/Thumbnail/${row.id}?t=${Date.now()}`;
              newImg.alt = '';
              newImg.loading = 'lazy';
              ph.replaceWith(newImg);
            }
          }
        }
      });
    } catch (e) {
      /* swallow — transient network error, try again next tick */
    }
  }

  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') startPolling();
    else stopPolling();
  });
  startPolling();

  function updateStatusBadge(card, statusName) {
    const labels = {
      Uploaded: 'Качен', Queued: 'В опашка', Processing: 'Обработва се',
      Extracted: 'Готов', Failed: 'Грешка',
    };
    card.dataset.status = statusName;
    const badge = card.querySelector('[data-role="status-badge"]');
    badge.className = `document-badge status-${statusName.toLowerCase()}`;
    badge.textContent = labels[statusName] || statusName;
    const cb = card.querySelector('[data-role="select"]');
    if (cb) {
      cb.disabled = !(statusName === 'Uploaded' || statusName === 'Failed');
      if (cb.disabled) cb.checked = false;
    }
  }

  // ---- New folder modal ---------------------------------------------------

  document.body.addEventListener('click', e => {
    const trigger = e.target.closest('[data-action="new-folder"]');
    if (!trigger) return;
    const parentField = newFolderDialog.querySelector('input[name="ParentFolderId"]');
    parentField.value = trigger.dataset.parent || '';
    newFolderDialog.querySelector('input[name="Name"]').value = '';
    newFolderDialog.querySelector('[data-role="new-folder-error"]').hidden = true;
    newFolderDialog.showModal();
  });

  newFolderDialog.querySelector('[data-action="cancel"]').addEventListener('click', () => {
    newFolderDialog.close();
  });

  newFolderDialog.querySelector('[data-role="new-folder-form"]').addEventListener('submit', async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const formData = new FormData(form);
    formData.append('__RequestVerificationToken', antiForgeryToken);
    const errorEl = form.querySelector('[data-role="new-folder-error"]');
    errorEl.hidden = true;
    const r = await fetch('/App/Folders/Create', {
      method: 'POST',
      body: formData,
      headers: { 'RequestVerificationToken': antiForgeryToken },
      credentials: 'same-origin',
    });
    if (!r.ok) {
      let msg = `Грешка ${r.status}`;
      try {
        const text = await r.text();
        try {
          const j = JSON.parse(text);
          if (j.error) msg = j.error;
        } catch {
          if (text) msg += ': ' + text.slice(0, 200);
        }
      } catch {}
      console.error('Folder create failed', r.status, r.statusText);
      errorEl.textContent = msg;
      errorEl.hidden = false;
      return;
    }
    newFolderDialog.close();
    location.reload();
  });

  // ---- Helpers ------------------------------------------------------------

  function escapeHtml(s) {
    return s.replace(/[&<>"']/g, m => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
    }[m]));
  }
})();
