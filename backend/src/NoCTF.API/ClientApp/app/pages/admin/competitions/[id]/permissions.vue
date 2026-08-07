<script setup lang="ts">
import { X } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminGetCompetitionPermissions,
  adminListCompetitionPermissionCandidates,
  adminTransferCompetitionOwner,
  adminUpdateCompetitionPermissions,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateResponse,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionsResponse,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canManagePermissions, refresh } = useCompetitionAdmin()

const permissions = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionsResponse | null>(null)
const candidates = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateResponse[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

const managerIds = ref<string[]>([])
const judgeIds = ref<string[]>([])
const observerIds = ref<string[]>([])

const candidateName = (id: string) => candidates.value.find(c => c.id === id)?.userName ?? id

async function load() {
  loading.value = true
  error.value = null
  const [perm, cand] = await Promise.all([
    adminGetCompetitionPermissions({ path: { competitionId } }),
    adminListCompetitionPermissionCandidates({ path: { competitionId } }),
  ])
  if (perm.error) {
    error.value = parseApiError(perm.error).message
    loading.value = false
    return
  }
  permissions.value = perm.data ?? null
  candidates.value = cand.data?.items ?? []
  managerIds.value = [...(perm.data?.managerIds ?? [])]
  judgeIds.value = [...(perm.data?.judgeIds ?? [])]
  observerIds.value = [...(perm.data?.observerIds ?? [])]
  loading.value = false
}

// ---- Assignment editing ----
const search = ref('')
const filteredCandidates = computed(() => {
  const q = search.value.trim().toLowerCase()
  const list = candidates.value.filter(c => c.id && c.id !== permissions.value?.ownerId)
  if (!q) return list.slice(0, 20)
  return list.filter(c => c.userName?.toLowerCase().includes(q)).slice(0, 20)
})

function assigned(id?: string): boolean {
  if (!id) return true
  return managerIds.value.includes(id) || judgeIds.value.includes(id) || observerIds.value.includes(id)
}

function add(role: 'manager' | 'judge' | 'observer', id?: string) {
  if (!id || assigned(id)) return
  if (role === 'manager') managerIds.value.push(id)
  else if (role === 'judge') judgeIds.value.push(id)
  else observerIds.value.push(id)
}

function remove(role: 'manager' | 'judge' | 'observer', id: string) {
  const list = role === 'manager' ? managerIds : role === 'judge' ? judgeIds : observerIds
  list.value = list.value.filter(x => x !== id)
}

const saving = ref(false)

async function save() {
  if (!permissions.value) return
  saving.value = true
  try {
    const { data, error } = await adminUpdateCompetitionPermissions({
      path: { competitionId },
      body: {
        managerIds: managerIds.value,
        judgeIds: judgeIds.value,
        observerIds: observerIds.value,
        expectedPermissionRevision: permissions.value.permissionRevision ?? 0,
      },
    })
    if (error) throw error
    permissions.value = data ?? permissions.value
    toast.success('权限已保存')
  }
  catch (e) {
    toastWriteError(e, load)
  }
  finally {
    saving.value = false
  }
}

// ---- Ownership transfer ----
const transferTarget = ref<string>('')
const transferConfirm = ref(false)
const transferring = ref(false)

async function transfer() {
  if (!transferTarget.value) return
  transferring.value = true
  try {
    const { error } = await adminTransferCompetitionOwner({
      path: { competitionId },
      body: { ownerId: transferTarget.value },
    })
    if (error) throw error
    toast.success('所有权已转让')
    transferConfirm.value = false
    await Promise.all([load(), refresh()])
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    transferring.value = false
  }
}

const roles = [
  { key: 'manager' as const, label: '管理员(Manager)', list: managerIds },
  { key: 'judge' as const, label: '裁判(Judge)', list: judgeIds },
  { key: 'observer' as const, label: '观察员(Observer)', list: observerIds },
]

onMounted(load)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="!canManagePermissions">
      <AlertDescription>只有竞赛负责人或平台管理员可以管理权限</AlertDescription>
    </Alert>
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="h-64 w-full" />

    <template v-else-if="permissions">
      <Card>
        <CardHeader>
          <CardTitle>协作权限</CardTitle>
          <CardDescription>
            负责人:{{ candidateName(permissions.ownerId ?? '') }} · 权限修订版本:{{ permissions.permissionRevision }}
          </CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-6">
          <div v-for="r in roles" :key="r.key" class="flex flex-col gap-2">
            <h3 class="text-sm font-medium">{{ r.label }}</h3>
            <div class="flex flex-wrap items-center gap-2">
              <Badge v-for="id in r.list.value" :key="id" variant="secondary" class="gap-1">
                {{ candidateName(id) }}
                <button
                  v-if="canManagePermissions"
                  type="button"
                  class="inline-flex"
                  :aria-label="`移除 ${candidateName(id)}`"
                  @click="remove(r.key, id)"
                >
                  <X class="size-3" />
                </button>
              </Badge>
              <span v-if="r.list.value.length === 0" class="text-sm text-muted-foreground">暂无</span>
            </div>
          </div>

          <template v-if="canManagePermissions">
            <Separator />
            <Field>
              <FieldLabel for="candidate-search">添加协作成员(搜索候选用户)</FieldLabel>
              <Input id="candidate-search" v-model="search" placeholder="按用户名搜索…" />
            </Field>
            <div class="flex flex-col gap-1">
              <div
                v-for="c in filteredCandidates"
                :key="c.id"
                class="flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm"
              >
                <span>
                  {{ c.userName }}
                  <span v-if="!c.emailVerified" class="text-muted-foreground">(邮箱未验证)</span>
                </span>
                <div class="flex items-center gap-1">
                  <template v-if="!assigned(c.id)">
                    <Button variant="ghost" size="sm" @click="add('manager', c.id)">设为管理员</Button>
                    <Button variant="ghost" size="sm" @click="add('judge', c.id)">设为裁判</Button>
                    <Button variant="ghost" size="sm" @click="add('observer', c.id)">设为观察员</Button>
                  </template>
                  <Badge v-else variant="outline">已分配</Badge>
                </div>
              </div>
              <p v-if="filteredCandidates.length === 0" class="text-sm text-muted-foreground">没有匹配的候选用户</p>
            </div>
            <div>
              <Button :disabled="saving" @click="save">
                <Spinner v-if="saving" data-icon="inline-start" />
                保存权限变更
              </Button>
            </div>
          </template>
        </CardContent>
      </Card>

      <Card v-if="canManagePermissions">
        <CardHeader>
          <CardTitle class="text-destructive">转让所有权</CardTitle>
          <CardDescription>将竞赛负责人身份转让给其他用户,转让后你不再拥有负责人权限</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-wrap items-center gap-2">
          <Select v-model="transferTarget">
            <SelectTrigger class="w-64">
              <SelectValue placeholder="选择新负责人" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="c in candidates.filter(c => c.id && c.id !== permissions?.ownerId)" :key="c.id" :value="c.id!">
                  {{ c.userName }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Button variant="destructive" :disabled="!transferTarget" @click="transferConfirm = true">
            转让所有权
          </Button>
        </CardContent>
      </Card>
    </template>

    <AlertDialog v-model:open="transferConfirm">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>转让所有权</AlertDialogTitle>
          <AlertDialogDescription>
            确认将竞赛所有权转让给「{{ candidateName(transferTarget) }}」?该操作立即生效。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>取消</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="transferring" @click="transfer">
            <Spinner v-if="transferring" data-icon="inline-start" />
            确认转让
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
