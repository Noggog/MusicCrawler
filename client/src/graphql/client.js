import { ApolloClient, InMemoryCache } from '@apollo/client';

const createApolloClient = () => {

  return new ApolloClient({
    uri: 'http://localhost:5000/graphql', // Replace with your backend GraphQL server URL
    cache: new InMemoryCache(),
  });
}

export default createApolloClient