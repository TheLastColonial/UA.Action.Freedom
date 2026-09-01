Feature: Box QR codes
    The deployed Freedom service issues a QR label for a box at /boxes/{id}/qr-code, renders a
    printable label at /boxes/{id}/label, and resolves a scanned token at /boxes/scan/{token}.
    Issuing and revoking are ordinary box writes (Administrator, Dispatcher, Loader); reading,
    the image and the label are ordinary box reads.

    The label ties the cardboard to its record and is the only thing a scanner needs. A box can
    be re-labelled: issuing a new code revokes the previous one, so a label that fell off in
    transit can be replaced and the old one stops working. The label crosses borders, so it
    carries a box number and nothing about where the box is going.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/boxes/scan/00000000-0000-0000-0000-000000000000"

Scenario: Reading a box QR code requires a token
    When I GET "/boxes/1/qr-code" without a token
    Then the response status is 401

Scenario: A ground officer is refused a box label
    Given I am authenticated as "groundofficer"
    When I GET "/boxes/1/label"
    Then the response status is 403

Scenario: An operator issues a QR code and it resolves to the box
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry", "postcode": "CV1 2AB" }
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/qr-code" on the remembered box
    Then the response status is 201
    Given I remember the issued QR token
    When I GET "/boxes/scan/{id}" for the remembered QR token
    Then the response status is 200
    And the response body field "city" is "Coventry"

Scenario: Re-issuing a QR code revokes the previous label
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/qr-code" on the remembered box
    Then the response status is 201
    Given I remember the issued QR token
    When I POST "/boxes/{id}/qr-code" on the remembered box
    Then the response status is 201
    When I GET "/boxes/scan/{id}" for the remembered QR token
    Then the response status is 404

Scenario: The printable label does not carry receiver detail
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry", "postcode": "CV1 2AB" }
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/qr-code" on the remembered box
    Then the response status is 201
    When I GET "/boxes/{id}/label" on the remembered box
    Then the response status is 200
    And the response body does not mention "Coventry"

Scenario: A label needs a QR code first
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    Given I remember the box
    When I GET "/boxes/{id}/label" on the remembered box
    Then the response status is 409

Scenario: Revoking a QR code makes it unresolvable
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/qr-code" on the remembered box
    Then the response status is 201
    Given I remember the issued QR token
    When I DELETE "/boxes/{id}/qr-code" on the remembered box
    Then the response status is 204
    When I GET "/boxes/scan/{id}" for the remembered QR token
    Then the response status is 404
