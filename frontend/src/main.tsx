import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClientProvider } from '@tanstack/react-query'
import './index.css'
import App from './App.tsx'
import { Toaster } from './components/Toaster.tsx'
import { createQueryClient } from './lib/queries.ts'

const queryClient = createQueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <Toaster>
        <App />
      </Toaster>
    </QueryClientProvider>
  </StrictMode>,
)
