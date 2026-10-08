// ==========================================================================
//  API Client - PW Extended App
// ==========================================================================

const API_BASE = '/api';

class ApiClient {
  constructor() {
    this.token = localStorage.getItem('dtu_token');
  }

  setToken(token) {
    this.token = token;
    if (token) {
      localStorage.setItem('dtu_token', token);
    } else {
      localStorage.removeItem('dtu_token');
    }
  }

  async request(endpoint, options = {}) {
    const headers = {
      'Content-Type': 'application/json',
      ...options.headers
    };

    if (this.token) {
      headers['Authorization'] = `Bearer ${this.token}`;
    }
    // Un header en undefined se omite (p. ej. Content-Type en subidas multipart).
    Object.keys(headers).forEach(k => headers[k] === undefined && delete headers[k]);

    const response = await fetch(`${API_BASE}${endpoint}`, {
      ...options,
      headers
    });

    if (response.status === 401) {
      this.setToken(null);
      window.dispatchEvent(new CustomEvent('dtu:auth-error'));
      throw new Error('Sesión expirada o no autorizada.');
    }

    if (!response.ok) {
      let errMessage = `Error ${response.status}`;
      try {
        const errorData = await response.json();
        errMessage = errorData.message || errMessage;
      } catch (e) {
        errMessage = response.statusText || errMessage;
      }
      throw new Error(errMessage);
    }

    // If it's a blob/download, handle differently
    const contentType = response.headers.get('content-type');
    if (contentType && contentType.includes('application/json')) {
      return await response.json();
    }
    return response;
  }

  // Auth
  async login(username, password) {
    const res = await this.request('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password })
    });
    this.setToken(res.token);
    return res;
  }

  async getMe() {
    return await this.request('/auth/me');
  }

  async changePassword(currentPassword, newPassword) {
    return await this.request('/auth/change-password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword, newPassword })
    });
  }

  // Empleados
  async getPadronInfo() {
    return await this.request('/empleados/info');
  }

  async getOptions() {
    return await this.request('/empleados/options');
  }

  async getPreview(fechaEvento, sociedades, estados, limit = 100) {
    return await this.request('/empleados/preview', {
      method: 'POST',
      body: JSON.stringify({ fechaEvento, sociedades, estados, limit })
    });
  }

  async processEmpleados(payload) {
    return await this.request('/empleados/process', {
      method: 'POST',
      body: JSON.stringify(payload)
    });
  }

  // Jobs History
  async getJobs(limit = 50) {
    return await this.request(`/jobs?limit=${limit}`);
  }

  getDownloadUrl(jobId, format) {
    return `${API_BASE}/jobs/${jobId}/download/${format}`;
  }

  async downloadJobFile(jobId, format, defaultFileName) {
    return this.downloadFile(`/jobs/${jobId}/download/${format}?_=${Date.now()}`, defaultFileName || null, `${format}_${jobId}`);
  }

  /** GET con parámetros de consulta (omite los vacíos). */
  async get(endpoint, params = {}) {
    const qs = new URLSearchParams();
    for (const [k, v] of Object.entries(params)) {
      if (v !== undefined && v !== null && v !== '') qs.append(k, v);
    }
    const q = qs.toString();
    return this.request(q ? `${endpoint}?${q}` : endpoint);
  }

  /** Sube archivos como multipart (campo "files"). */
  async uploadFiles(endpoint, files) {
    const form = new FormData();
    for (const f of files) form.append('files', f);
    return this.request(endpoint, {
      method: 'POST',
      body: form,
      // Sin Content-Type: el navegador pone el boundary del multipart.
      headers: { 'Content-Type': undefined }
    });
  }

  /** Descarga un archivo autenticado y lo guarda con el nombre que indique el servidor. */
  async downloadFile(endpoint, defaultFileName, fallbackName = 'descarga') {
    const response = await fetch(`${API_BASE}${endpoint}`, {
      headers: this.token ? { 'Authorization': `Bearer ${this.token}` } : {}
    });

    if (response.status === 401) {
      this.setToken(null);
      window.dispatchEvent(new CustomEvent('dtu:auth-error'));
      throw new Error('Sesión expirada: vuelva a iniciar sesión.');
    }

    if (!response.ok) {
      let errMessage = `Error ${response.status}`;
      try {
        const errorData = await response.json();
        errMessage = errorData.message || errMessage;
      } catch (e) { /* respuesta sin JSON */ }
      throw new Error(errMessage);
    }

    const disposition = response.headers.get('Content-Disposition') || '';
    const utf8Match = disposition.match(/filename\*=UTF-8''([^;]+)/i);
    const asciiMatch = disposition.match(/filename="?([^";]+)"?/i);
    const fileName = defaultFileName
      || (utf8Match ? decodeURIComponent(utf8Match[1]) : null)
      || (asciiMatch ? asciiMatch[1] : null)
      || fallbackName;

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.setAttribute('download', fileName);
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
      if (a.parentNode) a.parentNode.removeChild(a);
      URL.revokeObjectURL(url);
    }, 2000);

    return fileName;
  }

  // VIP list
  async getVipList() {
    return await this.request('/vip');
  }

  async createVip(data) {
    return await this.request('/vip', {
      method: 'POST',
      body: JSON.stringify(data)
    });
  }

  async updateVip(id, data) {
    return await this.request(`/vip/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data)
    });
  }

  async deleteVip(id) {
    return await this.request(`/vip/${id}`, {
      method: 'DELETE'
    });
  }

  // Admin
  async getUsers() {
    return await this.request('/admin/users');
  }

  async createUser(userData) {
    return await this.request('/admin/users', {
      method: 'POST',
      body: JSON.stringify(userData)
    });
  }

  async updateUser(userId, userData) {
    return await this.request(`/admin/users/${userId}`, {
      method: 'PUT',
      body: JSON.stringify(userData)
    });
  }

  async deleteUser(userId) {
    return await this.request(`/admin/users/${userId}`, {
      method: 'DELETE'
    });
  }

  async getConfig() {
    return await this.request('/admin/config');
  }

  async clearJobs() {
    return await this.request('/admin/jobs/clear', {
      method: 'POST'
    });
  }

  async updateConfig(configs) {
    return await this.request('/admin/config', {
      method: 'POST',
      body: JSON.stringify(configs)
    });
  }
}

window.api = new ApiClient();
