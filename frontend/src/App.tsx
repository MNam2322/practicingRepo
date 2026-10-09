import { Layout, ErrorBox, Loading } from './components/Layout';
import { api } from './lib/api';
import { useRoute } from './lib/router';
import { useApi } from './lib/useApi';
import { Home } from './pages/Home';
import { MyBookings } from './pages/MyBookings';
import { ResourcePage } from './pages/ResourcePage';

export default function App() {
  const route = useRoute();
  const [basics, reload] = useApi('basics', () => Promise.all([api.venue(), api.resources()]));
  const venue = basics.status === 'ready' ? basics.data[0] : null;
  const resources = basics.status === 'ready' ? basics.data[1] : [];

  let page;
  if (basics.status === 'loading') page = <Loading />;
  else if (basics.status === 'error') page = (
    <>
      <h1>Can't reach the booking service</h1>
      <ErrorBox message={basics.error} retry={reload} />
      <p className="muted">Start the backend first: <code>dotnet run --project backend/src/BookingApp.Api</code></p>
    </>
  );
  else if (route.name === 'home') page = <Home venue={venue!} resources={resources} />;
  else if (route.name === 'mine') page = <MyBookings />;
  else if (route.name === 'resource') {
    const resource = resources.find((r) => r.id === route.id);
    page = resource ? <ResourcePage key={`${resource.id}:${route.date ?? 'today'}`} venue={venue!} resource={resource} date={route.date} />
      : <><h1>Not found</h1><p><a href="#/">See all resources</a></p></>;
  } else page = <><h1>Page not found</h1><p><a href="#/">Back to booking</a></p></>;

  return <Layout venueName={venue?.name ?? ''} route={route}>{page}</Layout>;
}
