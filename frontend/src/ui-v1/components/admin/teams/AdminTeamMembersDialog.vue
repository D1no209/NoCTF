<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Loader2, Shield, User as UserIcon } from 'lucide-vue-next'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/ui-v1/components/ui/dialog'

interface TeamDto {
  id: string
  name: string
}

interface TeamMemberDto {
  userId: string
  userName: string
  role: string
}

defineProps<{
  open: boolean
  team: TeamDto | null
  teamMembers: TeamMemberDto[]
  loading: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
}>()

const { t } = useI18n()
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-[425px]">
      <DialogHeader>
        <DialogTitle>{{ t('admin.teams.membersDialog', { name: team?.name }) }}</DialogTitle>
        <DialogDescription>{{ t('admin.teams.membersDialogDescription') }}</DialogDescription>
      </DialogHeader>
      <div class="py-4">
        <div v-if="loading" class="flex items-center justify-center p-8 text-muted-foreground">
          <Loader2 class="mr-3 size-6 animate-spin" /> {{ t('common.loading') }}
        </div>
        <div v-else-if="teamMembers.length === 0" class="rounded-lg border border-dashed p-8 text-center text-sm text-muted-foreground">
          {{ t('admin.teams.noMembers') }}
        </div>
        <ul v-else class="space-y-2">
          <li
            v-for="member in teamMembers"
            :key="member.userId"
            class="flex items-center justify-between rounded-xl border bg-muted/30 p-3 transition-all hover:bg-muted/50"
          >
            <div class="flex items-center gap-3">
              <div class="flex size-8 items-center justify-center rounded-full border bg-background shadow-sm">
                <Shield v-if="member.role.toLowerCase() === 'captain'" class="size-4 text-primary" />
                <UserIcon v-else class="size-4 text-muted-foreground" />
              </div>
              <div class="flex flex-col">
                <span class="text-sm font-semibold leading-none">{{ member.userName }}</span>
                <span class="mt-1 text-[10px] text-muted-foreground">{{ member.userId.slice(0, 8) }}</span>
              </div>
            </div>
            <Badge :variant="member.role.toLowerCase() === 'captain' ? 'default' : 'secondary'" class="capitalize text-[10px] font-bold tracking-tighter">
              {{ member.role }}
            </Badge>
          </li>
        </ul>
      </div>
      <DialogFooter>
        <Button variant="outline" class="w-full sm:w-auto" @click="emit('update:open', false)">{{ t('common.close') }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>