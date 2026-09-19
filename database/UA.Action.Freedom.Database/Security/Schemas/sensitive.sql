/*
    The point of the database's shape is not the tables — it is the access control, so that
    docs/recommendations.md 4.4 is something the application is tested against rather than a
    paragraph of intent:

      dbo.*        convoy logistics — every role reads it
      sensitive.*  Ukrainian delivery addresses and receiver contacts — Ground Officer only

    A manifest listing precise delivery addresses is a targeting document and it crosses
    borders where it may be inspected or seized. The separation is enforced by the database
    (Security/Permissions.sql), so that widening it takes a deliberate, reviewable change.
*/
CREATE SCHEMA [sensitive] AUTHORIZATION [dbo];
