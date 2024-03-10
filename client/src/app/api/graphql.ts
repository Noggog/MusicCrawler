import { gql } from '@apollo/client';


export const GET_RECOMMENDATIONS = gql`
    query {
        recommendations {
            key {
                artistName
            }
            sourceArtists {
                artistName
            }
        }
    }
`;

//console.log("GET_RECOMMENDATIONS:"+JSON.stringify(GET_RECOMMENDATIONS))
console.log("aaa")

/*
query {
     recommendations {
         key {
             artistName
         }
         sourceArtists {
             artistName
         }
     }
 }
 */