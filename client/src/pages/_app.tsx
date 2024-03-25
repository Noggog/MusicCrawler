import { ApolloProvider } from '@apollo/client';
import createApolloClient from '../graphql/client';
import { AppProps } from 'next/app';
import "../app/globals.css"

function MyApp({ Component, pageProps }: AppProps) {
    const client = createApolloClient()
  return (
    <ApolloProvider client={client}>
      <Component {...pageProps} />
    </ApolloProvider>
  );
}

export default MyApp;