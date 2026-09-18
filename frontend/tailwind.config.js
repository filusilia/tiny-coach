/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx,vue}",
  ],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        dota: {
          bg: '#0B0E14',
          surface: '#11151F',
          panel: '#161B26',
          card: '#1C2230',
          hover: '#222B3D',
          border: '#2A3449',
          borderLight: '#3B4863',
          gold: '#E5C158',
          goldLight: '#F3D98B',
          cyan: '#38BDF8',
          red: '#EF4444',
          green: '#10B981',
          text: '#F1F5F9',
          muted: '#94A3B8',
          subtle: '#64748B',
        },
      },
    },
  },
  plugins: [],
}
