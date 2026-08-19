export type BoardRole = 'owner' | 'editor' | 'viewer';

export interface User {
  id: string;
  email: string;
  name: string;
}

export interface Subscription {
  plan: string;
  status: 'trialing' | 'active' | 'past_due' | 'canceled';
  active: boolean;
  trialEndsAt: string | null;
  currentPeriodEnd: string | null;
}

export interface Board {
  id: string;
  name: string;
  role: BoardRole;
  canEdit: boolean;
  canManage: boolean;
  memberCount: number;
  createdAt: string;
  modifiedAt: string;
}

export interface Member {
  userId: string;
  email: string;
  name: string;
  role: BoardRole;
  invitedAt: string;
}

/** Участник, который прямо сейчас открыл доску. */
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
