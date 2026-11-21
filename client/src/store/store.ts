import { configureStore } from "@reduxjs/toolkit";
import { useDispatch, useSelector } from "react-redux";
import settings, { persist } from "./settingsSlice";
import { tempoApi } from "./api";

export const store = configureStore({
  reducer: {
    settings,
    [tempoApi.reducerPath]: tempoApi.reducer,
  },
  middleware: (getDefault) => getDefault().concat(tempoApi.middleware),
});

let last = store.getState().settings;
store.subscribe(() => {
  const next = store.getState().settings;
  if (next !== last) {
    last = next;
    persist(next);
  }
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
export const useAppDispatch = useDispatch.withTypes<AppDispatch>();
export const useAppSelector = useSelector.withTypes<RootState>();
