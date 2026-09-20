/** @type {import('tailwindcss').Config} */
export default {
  content: [
    './index.html',
    './src/**/*.{js,ts,jsx,tsx,vue}',
  ],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        dota: {
          bg: '#121824',
          surface: '#182232',
          panel: '#1E293B',
          card: '#25334A',
          hover: '#2D3D57',
          border: '#334155',
          borderLight: '#475569',
          gold: '#E5C158',
          goldLight: '#F3D98B',
          cyan: '#38BDF8',
          red: '#EF4444',
          green: '#10B981',
          text: '#F8FAFC',
          muted: '#CBD5E1',
          subtle: '#94A3B8',
        },
      },
    },
  },
  plugins: [],
}
