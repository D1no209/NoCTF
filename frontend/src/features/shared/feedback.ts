import type { Ref } from 'vue'

// Minimal structural view of a vue-query mutation, so theme packages can
// attach feedback callbacks without importing @tanstack/vue-query.
export interface MutationAction<TVars = void> {
  isPending: Ref<boolean>
  /** Present on raw vue-query mutations; V1's reveal dialog reads it inline. */
  isError?: Ref<boolean>
  mutate: (variables: TVars, options?: {
    onSuccess?: (data?: unknown) => void
    onError?: (error: unknown) => void
  }) => void
  /** Present on raw vue-query mutations; V1's reveal flows reset before reuse. */
  reset?: () => void
}
