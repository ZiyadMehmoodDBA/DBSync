import axios from 'axios';
import type { LoginResponse } from '../types/auth';

const BASE = '/api/v1/auth';

export async function apiLogin(username: string, password: string): Promise<LoginResponse> {
  const { data } = await axios.post<LoginResponse>(`${BASE}/login`, { username, password });
  return data;
}

export async function apiRefresh(refreshToken: string): Promise<LoginResponse> {
  const { data } = await axios.post<LoginResponse>(`${BASE}/refresh`, { refreshToken });
  return data;
}

export async function apiLogout(refreshToken: string): Promise<void> {
  await axios.post(`${BASE}/logout`, { refreshToken });
}
