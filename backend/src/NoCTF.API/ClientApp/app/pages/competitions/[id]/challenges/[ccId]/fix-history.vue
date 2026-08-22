<script setup lang="ts">
definePageMeta({ middleware: 'auth' })

const route = useRoute()
const competitionId = route.params.id as string
const competitionChallengeId = route.params.ccId as string

await navigateTo({
  path: `/competitions/${competitionId}/challenges`,
  query: {
    challenge: competitionChallengeId,
    fixHistory: '1',
  },
}, { replace: true })
</script>
