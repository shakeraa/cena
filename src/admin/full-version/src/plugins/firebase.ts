import { initializeApp } from 'firebase/app'
import { connectAuthEmulator, getAuth } from 'firebase/auth'

const firebaseConfig = {
  apiKey: import.meta.env.VITE_FIREBASE_API_KEY,
  authDomain: import.meta.env.VITE_FIREBASE_AUTH_DOMAIN,
  projectId: import.meta.env.VITE_FIREBASE_PROJECT_ID,
  storageBucket: import.meta.env.VITE_FIREBASE_STORAGE_BUCKET,
  messagingSenderId: import.meta.env.VITE_FIREBASE_MESSAGING_SENDER_ID,
  appId: import.meta.env.VITE_FIREBASE_APP_ID,
}

export const firebaseApp = initializeApp(firebaseConfig)
export const firebaseAuth = getAuth(firebaseApp)

// RDY-056 §3: when VITE_FIREBASE_AUTH_EMULATOR_HOST is set (e.g. in the
// dockerised dev stack), connect the Auth SDK to the local emulator so
// sign-in/sign-out flows hit the emulator instead of real Firebase.
// Expected format: "http://localhost:9099" (scheme required by the SDK).
const emulatorHost = import.meta.env.VITE_FIREBASE_AUTH_EMULATOR_HOST
if (emulatorHost) {
  try {
    connectAuthEmulator(firebaseAuth, emulatorHost, { disableWarnings: true })
    // eslint-disable-next-line no-console
    console.info('[firebase-admin] Connected to Auth emulator at', emulatorHost)
  }
  catch (err) {
    // eslint-disable-next-line no-console
    console.error('[firebase-admin] Failed to connect to Auth emulator:', err)
  }
}
