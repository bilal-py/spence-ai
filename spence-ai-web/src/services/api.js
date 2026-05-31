const baseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5234/api';

async function handleResponse(response) {
  if (!response.ok) {
    let message = `Request failed with status ${response.status}`;

    try {
      const errorBody = await response.json();
      message =
        errorBody.message ||
        errorBody.title ||
        errorBody.detail ||
        message;
    } catch {
      try {
        const text = await response.text();
        if (text) message = text;
      } catch {
        /* keep default message */
      }
    }

    throw new Error(message);
  }

  const contentType = response.headers.get('content-type') ?? '';

  if (contentType.includes('application/json')) {
    return response.json();
  }

  return null;
}

async function request(url, options = {}) {
  try {
    const response = await fetch(url, options);
    return await handleResponse(response);
  } catch (error) {
    if (error instanceof TypeError) {
      throw new Error(
        'Unable to reach the Spence AI API. Ensure the backend is running at the configured URL.'
      );
    }
    throw error;
  }
}

export const api = {
  expenses: {
    async getSummary(year, month) {
      const params = new URLSearchParams();
      if (year != null) params.set('year', String(year));
      if (month != null) params.set('month', String(month));
      const query = params.toString();
      const url = `${baseUrl}/expenses/summary${query ? `?${query}` : ''}`;
      return request(url);
    },

    async getFiltered(year, month, categoryIds) {
      const params = new URLSearchParams();
      if (year != null) params.set('year', String(year));
      if (month != null) params.set('month', String(month));
      if (categoryIds?.length) params.set('categoryIds', categoryIds.join(','));
      const query = params.toString();
      const url = `${baseUrl}/expenses${query ? `?${query}` : ''}`;
      const data = await request(url);
      return Array.isArray(data) ? data : [];
    },

    async uploadPdf(file) {
      const formData = new FormData();
      formData.append('file', file);

      return request(`${baseUrl}/expenses/upload-pdf`, {
        method: 'POST',
        body: formData,
      });
    },
  },
};
