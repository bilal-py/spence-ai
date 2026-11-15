import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { lazy, StrictMode, Suspense } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter, RouterProvider } from 'react-router';
import { CategoryCreatorProvider } from './components/CategoryCreator';
import { Layout } from './components/Layout';
import { QuickAddProvider } from './components/QuickAdd';
import { ToastProvider } from './components/Toast';
import { TransactionEditorProvider } from './components/TransactionEditor';
import { Spinner } from './components/ui';
import './index.css';

// Pages are split so the chart library only loads on the Overview.
const Overview = lazy(() => import('./pages/Overview'));
const Transactions = lazy(() => import('./pages/Transactions'));
const Import = lazy(() => import('./pages/Import'));
const ImportReview = lazy(() => import('./pages/ImportReview'));
const Categories = lazy(() => import('./pages/Categories'));
const Settings = lazy(() => import('./pages/Settings'));
const Pots = lazy(() => import('./pages/Pots'));
const Wrapped = lazy(() => import('./pages/Wrapped'));
const Bills = lazy(() => import('./pages/Bills'));
const Ask = lazy(() => import('./pages/Ask'));

const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false, retry: 1 } },
});

const page = (el: React.ReactNode) => <Suspense fallback={<Spinner />}>{el}</Suspense>;

const router = createBrowserRouter([
  {
    element: (
      <CategoryCreatorProvider>
        <TransactionEditorProvider>
          <QuickAddProvider>
            <Layout />
          </QuickAddProvider>
        </TransactionEditorProvider>
      </CategoryCreatorProvider>
    ),
    children: [
      { index: true, element: page(<Overview />) },
      { path: 'transactions', element: page(<Transactions />) },
      { path: 'import', element: page(<Import />) },
      { path: 'import/:id', element: page(<ImportReview />) },
      { path: 'pots', element: page(<Pots />) },
      { path: 'bills', element: page(<Bills />) },
      { path: 'ask', element: page(<Ask />) },
      { path: 'wrapped', element: page(<Wrapped />) },
      { path: 'categories', element: page(<Categories />) },
      { path: 'settings', element: page(<Settings />) },
    ],
  },
]);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <RouterProvider router={router} />
      </ToastProvider>
    </QueryClientProvider>
  </StrictMode>,
);
