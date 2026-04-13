export type UserRole =
  | "SalesPerson"
  | "BomCreator"
  | "Accountant"
  | "ManagingDirector"
  | "Admin";

export interface AuthUser {
  userId: number;
  name: string;
  role: UserRole;
  branchId: number | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  role: UserRole;
  userId: number;
  name: string;
  branchId: number | null;
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface ApiError {
  message: string;
}
