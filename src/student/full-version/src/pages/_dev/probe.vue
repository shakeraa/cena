<script setup lang="ts">
import katex from 'katex'
import { HubConnectionBuilder } from '@microsoft/signalr'

// These imports prove the deps resolve. No runtime usage on this page.
const katexAvailable = typeof katex.render === 'function'
const signalrAvailable = typeof HubConnectionBuilder === 'function'
</script>

<template>
  <div class="pa-8">
    <h1>STU-W-00 Dev Probe</h1>
    <ul>
      <li>KaTeX loaded: {{ katexAvailable }}</li>
      <li>SignalR loaded: {{ signalrAvailable }}</li>
    </ul>
  </div>
</template>
