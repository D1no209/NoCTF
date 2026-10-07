<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionTeamBanScreenViewState } from '../../../features/competition/useCompetitionTeamBanScreen'
const props = defineProps<{ state: CompetitionTeamBanScreenViewState }>()
const { logoUrl, loading, error, canAppeal, appealStatus,
  appealOpen, statement, pending, appealError, refreshScreen, setAppealOpen, submitAppeal } = toRefs(props.state)
</script>

<template>
  <section data-team-banned-screen aria-labelledby="team-ban-title">
    <ScrollSurface class="team-ban-screen-scroll">
      <div class="team-ban-screen-content">
        <img :src="logoUrl" :alt="$t('common.label.noctf')" class="team-ban-screen-logo" width="520" height="155">
        <div role="status" aria-live="polite">
          <h1 id="team-ban-title" class="team-ban-screen-title">
            <span class="block">{{ $t('teamBanScreen.cheating') }}</span>
            <span class="block">{{ $t('teamBanScreen.banned') }}</span>
          </h1>
          <div class="team-ban-screen-description">
            <p>{{ $t('teamBanScreen.help') }}</p>
          </div>
        </div>
        <nav class="team-ban-screen-actions" :aria-label="$t('teamBanScreen.actions')">
          <Button v-if="canAppeal" type="button" variant="ghost" @click="setAppealOpen(true)">{{ $t('teamBanScreen.appeal') }}</Button>
          <span v-else-if="appealStatus === 'Submitted'" role="status">{{ $t('teamBanScreen.pending') }}</span>
          <span v-else-if="appealStatus === 'Upheld'">{{ $t('teamBanScreen.upheld') }}</span>
          <span v-else-if="!loading && !error">{{ $t('teamBanScreen.captain') }}</span>
          <Button type="button" variant="ghost" as-child><NuxtLink to="/competitions">{{ $t('teamBanScreen.return') }}</NuxtLink></Button>
          <Button type="button" variant="ghost" :disabled="loading" @click="refreshScreen">{{ $t('teamBanScreen.refresh') }}</Button>
        </nav>
        <Alert v-if="error" variant="destructive" class="mt-4 max-w-xl bg-card"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
      </div>
    </ScrollSurface>
    <Dialog :open="appealOpen" @update:open="setAppealOpen">
      <DialogContent>
        <DialogHeader><DialogTitle>{{ $t('teamBanScreen.appeal') }}</DialogTitle><DialogDescription>{{ $t('teamBanScreen.appealDescription') }}</DialogDescription></DialogHeader>
        <UiForm validation="feature" @submit.prevent="submitAppeal">
          <FieldGroup>
            <Field><FieldLabel for="team-ban-statement">{{ $t('teamBanScreen.statement') }}</FieldLabel><Textarea id="team-ban-statement" v-model="statement" :disabled="pending" maxlength="512" :aria-invalid="!!appealError" /></Field>
            <Alert v-if="appealError" variant="destructive"><AlertDescription>{{ $message(appealError) }}</AlertDescription></Alert>
            <DialogFooter><Button type="button" variant="outline" :disabled="pending" @click="setAppealOpen(false)">{{ $t('common.action.cancel') }}</Button><Button type="submit" :disabled="pending"><Spinner v-if="pending" />{{ $t('teamBanScreen.submit') }}</Button></DialogFooter>
          </FieldGroup>
        </UiForm>
      </DialogContent>
    </Dialog>
  </section>
</template>
