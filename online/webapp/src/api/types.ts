export type BoardRole = 'owner' | 'editor' | 'viewer';

export interface User {
  id: string;
  email: string;
  lastName: string;
  firstName: string;
  birthDate: string;
  trialUsed: boolean;
}

export interface Subscription {
  kind: 'trial' | 'paid';
  planDays: number;
  status: 'active' | 'canceled';
  active: boolean;
  expiresAt: string;
  autoRenew: boolean;
}

export interface Plan {
  days: number;
  price: number;
  title: string;
}

export interface Board {
  id: string;
  name: string;
  role: BoardRole;
  canEdit: boolean;
  canManage: boolean;
  /** Доска чужая — попали в неё по приглашению или по ссылке. */
  invited: boolean;
  memberCount: number;
  /** До какого момента можно менять доску. null — без ограничения. */
  editUntil: string | null;
  createdAt: string;
  modifiedAt: string;
}

export interface BoardPage {
  items: Board[];
  page: number;
  pageSize: number;
  total: number;
}

export interface Member {
  userId: string;
  email: string;
  name: string;
  role: BoardRole;
  viaLink: boolean;
  editUntil: string | null;
  invitedAt: string;
}

export interface Invite {
  id: string;
  role: BoardRole;
  createdAt: string;
  expiresAt: string;
  uses: number;
  editDays: number | null;
  /** Приходит только при создании: в базе лежит хеш, восстановить ссылку нельзя. */
  url: string | null;
}

export interface Participant {
  userId: string;
  name: string;
  color: string;
  role: BoardRole;
}

export interface AuthResponse {
  token: string;
  user: User;
  subscription: Subscription | null;
}

export interface BoardJoined {
  boardId: string;
  name: string;
  role: BoardRole;
  canEdit: boolean;
  canManage: boolean;
  participants: Participant[];
  members: { userId: string; email: string; name: string; role: BoardRole }[];
}
