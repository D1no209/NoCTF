<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformBotsPageViewState } from '~/features/routes/admin/platform/useAdminPlatformBotsPage'

const viewProps = defineProps<{ state: AdminPlatformBotsPageViewState }>()
const { Bot, Copy, KeyRound, Plus, ROLE_LABELS, bots, loading, loadError, createOpen, creating, botName, botRole, openCreate, createBot, issueOpen, issuing, issueTarget, expiresInSeconds, issueReason, issuedToken, openIssue, issueToken, copyToken, AdminDateTime, onClickCreateOpen, onClickIssueOpen } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex justify-end">
      <Button @click="openCreate">
        <Plus data-icon="inline-start" /> {{ $t('ui.createBot') }} </Button>
    </div>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>

    <Card v-if="loading">
      <CardContent class="flex flex-col gap-3 pt-6">
        <Skeleton v-for="i in 3" :key="i" class="h-10 w-full" />
      </CardContent>
    </Card>

    <Empty v-else-if="bots.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.noBotYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('ui.createABotServiceAccountAndIssueAnAccessToken') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Card v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('ui.name') }}</TableHead>
            <TableHead>{{ $t('ui.role') }}</TableHead>
            <TableHead>{{ $t('ui.creationTime') }}</TableHead>
            <TableHead class="text-right">{{ $t('ui.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="bot in bots" :key="bot.id">
            <TableCell class="font-medium">
              <span class="inline-flex items-center gap-2">
                <Bot class="size-4 text-muted-foreground" />
                {{ bot.userName }}
              </span>
            </TableCell>
            <TableCell>
              <Badge :variant="bot.role === 'Administrator' ? 'default' : 'secondary'">
                {{ ROLE_LABELS[String(bot.role)] ? $t(ROLE_LABELS[String(bot.role)]!) : bot.role }}
              </Badge>
            </TableCell>
            <TableCell>
              <component :is="AdminDateTime" :value="bot.createdAt" />
            </TableCell>
            <TableCell class="text-right">
              <Button size="sm" variant="outline" @click="openIssue(bot)">
                <KeyRound data-icon="inline-start" /> {{ $t('ui.issueToken') }} </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <Dialog v-model:open="createOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.createBot') }}</DialogTitle>
          <DialogDescription>{{ $t('ui.botIsASpecialServiceAccountForWhichAccessTokens') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="bot-name">{{ $t('ui.name') }}</FieldLabel>
            <Input id="bot-name" v-model="botName" required maxlength="50" :placeholder="$t('ui.forExampleScoreboardSync')" />
          </Field>
          <Field>
            <FieldLabel for="bot-role">{{ $t('ui.role') }}</FieldLabel>
            <Select v-model="botRole">
              <SelectTrigger id="bot-role" class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem value="User">{{ $t('ui.user') }}</SelectItem>
                  <SelectItem value="Organizer">{{ $t('ui.organizer') }}</SelectItem>
                  <SelectItem value="Administrator">{{ $t('ui.administrator') }}</SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickCreateOpen(false)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="creating || !botName.trim()" @click="createBot">
            <Spinner v-if="creating" data-icon="inline-start" /> {{ $t('ui.create') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog :open="issueOpen" @update:open="onClickIssueOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.issueAccessToken') }}</DialogTitle>
          <DialogDescription>{{ $t('ui.issueABotAccessTokenFor', { user: issueTarget?.userName ?? '-' }) }}</DialogDescription>
        </DialogHeader>
        <template v-if="!issuedToken">
          <FieldGroup>
            <Field>
              <FieldLabel for="token-ttl">{{ $t('ui.validityPeriodSeconds') }}</FieldLabel>
              <NumberInput id="token-ttl" v-model.number="expiresInSeconds" min="60" max="31536000" step="60" required />
              <FieldDescription>{{ $t('ui.tokenLifetimeRange') }}</FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="bot-token-reason">{{ $t('ui.issuanceReason') }}</FieldLabel>
              <Textarea id="bot-token-reason" v-model="issueReason" minlength="3" maxlength="500" rows="3" required />
              <FieldDescription>{{ $t('ui.issuanceReasonAuditNotice') }}</FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="onClickIssueOpen(false)">{{ $t('ui.cancel') }}</Button>
            <Button :disabled="issuing || issueReason.trim().length < 3 || expiresInSeconds < 60 || expiresInSeconds > 31536000" @click="issueToken">
              <Spinner v-if="issuing" data-icon="inline-start" /> {{ $t('ui.issue') }} </Button>
          </DialogFooter>
        </template>
        <template v-else>
          <Alert>
            <AlertDescription>{{ $t('ui.theTokenIsOnlyDisplayedOncePleaseCopyAndSave') }}</AlertDescription>
          </Alert>
          <FieldGroup>
            <Field>
              <FieldLabel for="issued-token">{{ $t('ui.accessToken') }}</FieldLabel>
              <div class="flex items-center gap-2">
                <Input id="issued-token" :model-value="issuedToken.accessToken" readonly class="font-mono text-xs" />
                <Button size="icon" variant="outline" :aria-label="$t('ui.copyToken')" @click="copyToken">
                  <Copy />
                </Button>
              </div>
              <FieldDescription> {{ $t('ui.expirationDate') }}<component :is="AdminDateTime" :value="issuedToken.expiresAt" />
              </FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button @click="onClickIssueOpen(false)">{{ $t('ui.iHaveSavedClose') }}</Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>
  </div>
</template>
