<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { NoCtfapiEndpointsChallengesAvailableChallengeActionResponse as Action } from '~/api'
import { usePersistentFixSubmit } from './usePersistentFixSubmit'
import View from '~/components/views/challenges/PersistentFixSubmitView.vue'
const props = defineProps<{ competitionId: string; competitionChallengeId: string; action: Action }>()
const emit = defineEmits<{ changed: [] }>()
const FeatureView = defineFeatureView(View, () => usePersistentFixSubmit(props, emit))
</script>
<template><FeatureView /></template>
