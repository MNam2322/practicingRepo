import { Layout } from './components/Layout';
import { ErrorNotice } from './components/Notice';
import { CardsSkeleton } from './components/Skeleton';
import { describeError, useBasics } from './lib/queries';
import { useRoute } from './lib/router';
import { Home } from './pages/Home';
import { BookingPage } from './pages/BookingPage';
import { MyBookings } from './pages/MyBookings';
import { ResourcePage } from './pages/ResourcePage';
import { SignInPage, SignUpPage } from './pages/SignInPage';
import { StaffPage } from './pages/StaffPage';
import { AdminPage } from './pages/admin/AdminPage';

export default function App() {
  const route = useRoute();
  const basics = useBasics();
  const venue = basics.data?.[0] ?? null;
  const resources = basics.data?.[1] ?? [];

  let page;
  if (basics.isPending) page = <CardsSkeleton />;
  else if (basics.isError) page = (
    <>
      <h1>Can't reach the booking service</h1>
      <ErrorNotice {...describeError(basics.error)} retry={() => void basics.refetch()} />
      <p className="muted">Start the backend first: <code>dotnet run --project backend/src/BookingApp.Api</code></p>
    </>
  );
  else if (route.name === 'home') page = <Home venue={venue!} resources={resources} />;
  else if (route.name === 'mine') page = <MyBookings />;
  else if (route.name === 'signin') page = <SignInPage key={route.next} next={route.next} />;
  else if (route.name === 'signup') page = <SignUpPage key={route.next} next={route.next} />;
  else if (route.name === 'admin') page = <AdminPage venue={venue!} resources={resources} section={route.section} />;
  else if (route.name === 'staff') page = <StaffPage key={route.date ?? 'today'} venue={venue!} date={route.date} />;
  else if (route.name === 'booking') page = <BookingPage key={route.id} id={route.id} venue={venue!} />;
  else if (route.name === 'resource') {
    const resource = resources.find((r) => r.id === route.id);
    page = resource ? <ResourcePage key={`${resource.id}:${route.date ?? 'today'}:${route.view}:${route.at ?? ''}`} venue={venue!} resource={resource} date={route.date} view={route.view} at={route.at} />
      : <><h1>Not found</h1><p><a href="#/">See all resources</a></p></>;
  } else page = <><h1>Page not found</h1><p><a href="#/">Back to booking</a></p></>;

  return <Layout venueName={venue?.name ?? ''} route={route}>{page}</Layout>;
}
