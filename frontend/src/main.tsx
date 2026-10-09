import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { Toaster } from './components/Toaster.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Toaster>
      <App />
    </Toaster>
  </StrictMode>,
)
