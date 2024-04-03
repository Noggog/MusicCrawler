import { gql } from '@apollo/client'

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
`

export const ACCUMULATE_RECOMMENDATIONS = gql`
query {
     accumulateRecommendations
 }
`

