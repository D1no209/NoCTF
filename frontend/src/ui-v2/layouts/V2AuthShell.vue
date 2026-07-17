<script setup lang="ts">
</script>

<template>
  <div class="ui-v2 v2-auth">
    <div class="v2-auth__inner">
      <header class="v2-auth__brand">
        <img src="/logo.png" alt="NoCTF" />
        <strong>NoCTF</strong>
        <span>Command Suite</span>
      </header>

      <slot />

      <footer class="v2-auth__footer">
        <span>Neon Command UI package / v0.2.0</span>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.v2-auth {
  display: grid;
  min-height: 100dvh;
  place-items: center;
  padding: 42px 18px;
}

.v2-auth__inner {
  display: grid;
  width: min(430px, 100%);
  justify-items: center;
  gap: 22px;
}

.v2-auth__brand {
  display: grid;
  justify-items: center;
  gap: 6px;
}

.v2-auth__brand img { width: 58px; height: auto; filter: brightness(0) opacity(0.72); }
.v2-auth__brand strong { margin-top: 6px; color: var(--v2-text); font-size: 18px; font-weight: 600; letter-spacing: 0.01em; }
.v2-auth__brand span { color: var(--v2-primary); font-family: var(--v2-font-mono); font-size: 9px; font-weight: 600; letter-spacing: 0.1em; }

.v2-auth__inner > :not(.v2-auth__brand):not(.v2-auth__footer) { width: 100%; }

.v2-auth__footer span {
  color: var(--v2-text-faint);
  font-family: var(--v2-font-mono);
  font-size: 9px;
  letter-spacing: 0.06em;
}
</style>
