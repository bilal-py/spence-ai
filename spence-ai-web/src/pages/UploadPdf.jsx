import { useCallback, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../services/api';

export default function UploadPdf() {
  const navigate = useNavigate();
  const inputRef = useRef(null);
  const [isDragging, setIsDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState(null);
  const [successMessage, setSuccessMessage] = useState(null);

  const processFile = useCallback(
    async (file) => {
      if (!file) return;

      if (file.type !== 'application/pdf' && !file.name.toLowerCase().endsWith('.pdf')) {
        setError('Please select a valid PDF file.');
        return;
      }

      setUploading(true);
      setError(null);
      setSuccessMessage(null);

      try {
        const response = await api.expenses.uploadPdf(file);
        const categorizedCount = Array.isArray(response?.expenses)
          ? response.expenses.length
          : response?.processedCount ?? response?.count;

        setSuccessMessage(
          categorizedCount != null
            ? `Successfully categorized ${categorizedCount} transaction(s).`
            : 'Statement processed successfully.'
        );

        navigate('/', { replace: true, state: { refreshDashboard: true } });
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Upload failed. Please try again.');
      } finally {
        setUploading(false);
        setIsDragging(false);
      }
    },
    [navigate]
  );

  const onDragEnter = (event) => {
    event.preventDefault();
    event.stopPropagation();
    if (!uploading) setIsDragging(true);
  };

  const onDragLeave = (event) => {
    event.preventDefault();
    event.stopPropagation();
    setIsDragging(false);
  };

  const onDragOver = (event) => {
    event.preventDefault();
    event.stopPropagation();
    if (!uploading) setIsDragging(true);
  };

  const onDrop = (event) => {
    event.preventDefault();
    event.stopPropagation();
    setIsDragging(false);
    if (uploading) return;

    const file = event.dataTransfer.files?.[0];
    processFile(file);
  };

  const onFileChange = (event) => {
    const file = event.target.files?.[0];
    processFile(file);
    event.target.value = '';
  };

  const zoneClasses = [
    'relative flex min-h-[280px] cursor-pointer flex-col items-center justify-center rounded-2xl border-2 border-dashed px-6 py-12 text-center transition-all',
    uploading
      ? 'cursor-wait border-teal-300 bg-teal-50/60'
      : isDragging
        ? 'border-teal-500 bg-teal-50 scale-[1.01] shadow-md'
        : 'border-slate-300 bg-white hover:border-teal-400 hover:bg-slate-50',
  ].join(' ');

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Upload PDF</h2>
        <p className="mt-1 text-sm text-slate-500">
          Drop a credit card statement to extract and categorize transactions automatically.
        </p>
      </div>

      <div
        className={zoneClasses}
        onDragEnter={onDragEnter}
        onDragLeave={onDragLeave}
        onDragOver={onDragOver}
        onDrop={onDrop}
        onClick={() => !uploading && inputRef.current?.click()}
        onKeyDown={(event) => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            if (!uploading) inputRef.current?.click();
          }
        }}
        role="button"
        tabIndex={0}
        aria-busy={uploading}
        aria-label="Upload PDF statement"
      >
        <input
          ref={inputRef}
          type="file"
          accept=".pdf,application/pdf"
          className="sr-only"
          onChange={onFileChange}
          disabled={uploading}
        />

        {uploading ? (
          <>
            <div
              className="mb-4 h-12 w-12 animate-spin rounded-full border-4 border-teal-200 border-t-teal-700"
              aria-hidden="true"
            />
            <p className="text-lg font-semibold text-teal-900">Processing statement…</p>
            <p className="mt-2 max-w-sm text-sm text-teal-700/80">
              Extracting transactions and applying AI categorization. This may take a moment.
            </p>
          </>
        ) : isDragging ? (
          <>
            <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-teal-100 text-2xl text-teal-700">
              ↓
            </div>
            <p className="text-lg font-semibold text-teal-900">Release to upload</p>
            <p className="mt-2 text-sm text-slate-500">Your PDF will be sent to the Spence AI API.</p>
          </>
        ) : (
          <>
            <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-slate-100 text-2xl text-slate-600">
              PDF
            </div>
            <p className="text-lg font-semibold text-slate-900">
              Drag & drop your statement here
            </p>
            <p className="mt-2 text-sm text-slate-500">or click to browse — PDF files only</p>
          </>
        )}
      </div>

      {error ? (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      ) : null}

      {successMessage ? (
        <div className="rounded-lg border border-teal-200 bg-teal-50 px-4 py-3 text-sm text-teal-900">
          {successMessage}
        </div>
      ) : null}
    </div>
  );
}
