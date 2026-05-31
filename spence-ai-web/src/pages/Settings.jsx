import { useEffect, useState } from 'react';

export default function Settings() {
  const [storageMode, setStorageMode] = useState('Local');
  const [aiEngine, setAiEngine] = useState('Gemini');
  const [apiKey, setApiKey] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let cancelled = false;

    async function loadSettings() {
      setLoading(true);
      setError(null);

      try {
        const response = await fetch('http://localhost:5234/api/settings');
        if (!response.ok) {
          throw new Error(`Failed to fetch settings: ${response.status}`);
        }
        const data = await response.json();
        if (!cancelled) {
          setStorageMode(data.storageMode || 'Local');
          setAiEngine(data.aiEngine || 'Gemini');
          setApiKey(data.apiKey || '');
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load settings');
          console.error('Settings load error:', err);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    loadSettings();

    return () => {
      cancelled = true;
    };
  }, []);

  const handleSave = async () => {
    setSaving(true);
    setError(null);

    try {
      const response = await fetch('http://localhost:5234/api/settings/update', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          storageMode,
          aiEngine,
          apiKey,
        }),
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.message || `Failed to save settings: ${response.status}`);
      }

      // Optionally show a success message
      alert('Settings saved successfully!');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save settings');
      console.error('Settings save error:', err);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="min-h-[calc(100vh-64px)] flex items-center justify-center">
        <div className="animate-spin rounded-full border-4 border-teal-700 border-t-transparent h-12 w-12"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="min-h-[calc(100vh-64px)] flex flex-col items-center justify-center p-6">
        <h2 className="text-2xl font-bold text-red-800 mb-4">Error Loading Settings</h2>
        <p className="text-red-600 text-center mb-6">{error}</p>
        <button
          onClick={() => window.location.reload()}
          className="px-4 py-2 bg-teal-700 text-white rounded hover:bg-teal-800 transition-colors"
        >
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="min-h-[calc(100vh-64px)] bg-slate-50 py-8">
      <div className="mx-auto max-w-2xl px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold text-slate-900 mb-8 text-center">Application Settings</h1>

        <div className="bg-white rounded-xl shadow-md p-6 space-y-6">
          <div className="space-y-4">
            <p className="text-lg font-medium text-slate-800">Storage Configuration</p>
            <div className="space-y-2">
              <label className="block text-sm font-medium text-slate-700 mb-1">
                Storage Mode
              </label>
              <select
                value={storageMode}
                onChange={(e) => setStorageMode(e.target.value)}
                className="w-full px-4 py-2 border border-slate-300 rounded-md focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500"
                disabled={saving}
              >
                <option value="Local">Local (IndexedDB)</option>
                <option value="Cloud">Cloud (Remote Database)</option>
              </select>
            </div>

            <div className="space-y-2">
              <label className="block text-sm font-medium text-slate-700 mb-1">
                AI Engine
              </label>
              <select
                value={aiEngine}
                onChange={(e) => setAiEngine(e.target.value)}
                className="w-full px-4 py-2 border border-slate-300 rounded-md focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500"
                disabled={saving}
              >
                <option value="Gemini">Google Gemini</option>
                <option value="Ollama">Ollama (Local)</option>
              </select>
            </div>
          </div>

          <div className="space-y-4">
            <p className="text-lg font-medium text-slate-800">API Configuration</p>
            <div className="space-y-2">
              <label className="block text-sm font-medium text-slate-700 mb-1">
                API Key
              </label>
              <input
                type="password"
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                className="w-full px-4 py-2 border border-slate-300 rounded-md focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500"
                placeholder="Enter your API key"
                disabled={saving}
              />
              {apiKey.length > 0 && (
                <p className="mt-1 text-sm text-teal-600">
                  API key is set (hidden for security)
                </p>
              )}
            </div>
          </div>

          <div className="pt-4 border-t border-slate-200">
            <button
              onClick={handleSave}
              disabled={saving}
              className="w-full px-6 py-3 bg-teal-700 text-white font-medium rounded-lg hover:bg-teal-800 transition-colors flex items-center justify-center gap-2"
            >
              {saving ? (
                <>
                  <svg className="animate-spin h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z"></path>
                  </svg>
                  <span>Saving...</span>
                </>
              ) : (
                <>
                  <svg className="h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12l2 2 4-4M6 18l6-6-6-6"></path>
                  </svg>
                  <span>Save Settings</span>
                </>
              )}
            </button>
          </div>
        </div>

        <div className="mt-8 text-center text-slate-500">
          <p className="text-sm">
            Note: Changes to storage mode may require application restart to take full effect.
          </p>
        </div>
      </div>
    </div>
  );
}