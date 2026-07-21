import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../api/client';
import { getMe } from '../../api/accounts';
import type { UserResponse } from '../../api/types';

export const CURRENT_USER_QUERY_KEY = ['currentUser'];

export function useCurrentUser() {
  return useQuery<UserResponse | null>({
    queryKey: CURRENT_USER_QUERY_KEY,
    queryFn: async () => {
      try {
        return await getMe();
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) {
          return null;
        }
        throw err;
      }
    },
  });
}
