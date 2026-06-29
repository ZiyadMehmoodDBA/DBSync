export interface UserProfile {
  username: string;
  roles: string[];
  expiresAt: string;
}

export interface AuthState {
  accessToken: string | null;
  user: UserProfile | null;
  isInitializing: boolean;
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  setTokens: (token: string, refreshToken: string, profile: UserProfile) => void;
  clearTokens: () => void;
}

export interface LoginResponse {
  token: string;
  refreshToken: string;
  expiresAt: string;
}
