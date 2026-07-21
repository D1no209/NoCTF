<script setup lang="ts">
import { computed, ref } from 'vue'
import { useAdminUsersPage, type AdminUserDto } from '@/features/admin/useAdminUsersPage'
import CommandAdminUsersWorkspace, { type CommandAdminUser } from '../components/CommandAdminUsersWorkspace.vue'

const {
  users: userData,
  isLoading,
  isError,
  error,
  refetch,
  changeRoleMutation,
  resetPasswordMutation,
} = useAdminUsersPage()

const roleDialogOpen = ref(false)
const passwordDialogOpen = ref(false)
const selectedUser = ref<CommandAdminUser | null>(null)
const newRole = ref('user')
const newPassword = ref('')
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const users = computed<CommandAdminUser[]>(() => (userData.value ?? [])
  .filter((user): user is AdminUserDto & { id: string } => Boolean(user.id))
  .map(user => ({
    id: user.id,
    userName: user.userName?.trim() || 'Unnamed user',
    email: user.email?.trim() || '-',
    role: user.role?.trim() || 'user',
  })))

const workspaceState = computed<'loading' | 'error' | 'ready'>(() => {
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const cause = error.value
  return cause instanceof Error ? cause.message : undefined
})

function openRole(user: CommandAdminUser) {
  selectedUser.value = user
  newRole.value = user.role.toLowerCase()
  roleDialogOpen.value = true
}

function openPassword(user: CommandAdminUser) {
  selectedUser.value = user
  newPassword.value = ''
  passwordDialogOpen.value = true
}

function saveRole() {
  const user = selectedUser.value
  if (!user)
    return
  changeRoleMutation.mutate({ id: user.id, role: newRole.value }, {
    onSuccess: () => {
      roleDialogOpen.value = false
      operationTone.value = 'success'
      operationMessage.value = 'User role updated.'
    },
    onError: (cause) => {
      operationTone.value = 'danger'
      operationMessage.value = cause instanceof Error ? cause.message : 'Unable to update the user role.'
    },
  })
}

function savePassword() {
  const user = selectedUser.value
  if (!user)
    return
  resetPasswordMutation.mutate({ id: user.id, password: newPassword.value }, {
    onSuccess: () => {
      passwordDialogOpen.value = false
      newPassword.value = ''
      operationTone.value = 'success'
      operationMessage.value = 'Password reset successfully.'
    },
    onError: (cause) => {
      operationTone.value = 'danger'
      operationMessage.value = cause instanceof Error ? cause.message : 'Unable to reset the password.'
    },
  })
}

function retry() {
  void refetch()
}
</script>

<template>
  <CommandAdminUsersWorkspace
    v-model:role-dialog-open="roleDialogOpen"
    v-model:password-dialog-open="passwordDialogOpen"
    v-model:new-role="newRole"
    v-model:new-password="newPassword"
    :state="workspaceState"
    :error-message="errorMessage"
    :users="users"
    :selected-user="selectedUser"
    :role-pending="changeRoleMutation.isPending.value"
    :password-pending="resetPasswordMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @retry="retry"
    @open-role="openRole"
    @open-password="openPassword"
    @save-role="saveRole"
    @save-password="savePassword"
  />
</template>
