import type { Ref } from 'vue'

// Minimal structural view of a vue-query mutation, so theme packages can
// attach feedback callbacks without importing @tanstack/vue-query.
export interface MutationAction<TVars = void> {
  isPending: Ref<boolean>
  mutate: (variables: TVars, options?: {
    onSuccess?: (data?: unknown) => void
    onError?: (error: unknown) => void
  }) => void
}
